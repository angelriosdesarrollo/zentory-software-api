using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Application.Organization.Services;

// Aviso previo de la transferencia para el owner (PLAN-0018 RF-77; SR-0018.46). La respuesta no expone conteos de
// organizaciones del destino: solo Blocked y BlockReason (test de contrato sobre estas propiedades).
public sealed record OwnershipTransferPreviewDto(
    string                          CurrentPlan,
    string                          NewPlan,
    IReadOnlyList<string>           LostFeatures,
    IReadOnlyList<ExceededLimitDto> ExceededLimits,
    bool                            Blocked,
    string?                         BlockReason,
    string                          PreviewHash);

// Un límite cuantitativo del plan nuevo que la organización ya supera: uso actual y límite nuevo (RF-77).
public sealed record ExceededLimitDto(string FeatureKey, int Usage, int Limit);

// Validaciones del destino, cálculo del aviso y su previewHash (RF-64 a, b, d; RF-77; D-33). Lo usan el aviso
// (GetTransferOwnershipPreviewQuery), la propuesta (ProposeOwnershipTransferCommand), el aviso del destino
// (ListMyOwnershipTransfersQuery) y la aceptación (AcceptOwnershipTransferCommand): el hash sale siempre de ComputeHash,
// la única función que lo calcula.
public static class OwnershipTransferPreview
{
    public const string UserIdField          = "userId";
    public const string SelfTransferMessage  = "No puedes transferirte la organización a ti mismo.";
    public const string QuotaExceededReason  = "QUOTA_EXCEEDED";
    public const string TargetResourceType   = "OrganizationMember";

    // Orden de los planes de menor a mayor: una bajada pierde las funciones de los planes por encima del nuevo.
    private static readonly string[] PlanOrder = [Plan.Free, Plan.Pro, Plan.Studio];

    // Límites de la organización que el aviso compara (RF-77). max_owned_orgs es del destino y va en Blocked.
    private static readonly string[] OrganizationLimitKeys =
    [
        PlanLimits.FeatureKeys.MaxClients,
        PlanLimits.FeatureKeys.MaxInvoicesMonth,
        PlanLimits.FeatureKeys.MaxOrgMembers,
        PlanLimits.FeatureKeys.MaxProjects,
        PlanLimits.FeatureKeys.MaxCollaborators,
    ];

    // Destino válido de RF-64 a, en este orden: miembro vigente de la organización (si no, 404 sin decir por qué), distinto
    // del owner (422) y con email verificado (403 EMAIL_NOT_VERIFIED). El filtro de tenant ya limita a la organización
    // activa; el predicado explícito lo repite.
    public static async Task<User> ResolveTargetAsync(
        IZentoryDbContext db, Guid organizationId, Guid ownerId, Guid targetUserId, CancellationToken ct)
    {
        var isMember = await db.OrganizationMembers.AnyAsync(m =>
            m.OrganizationId == organizationId && m.UserId == targetUserId && m.DeletedAt == null, ct);
        var target = isMember
            ? await db.Users.FirstOrDefaultAsync(u => u.UserId == targetUserId, ct)
            : null;
        if (target is null)
            throw new NotFoundException(TargetResourceType, targetUserId);

        if (targetUserId == ownerId)
            throw new ValidationException([new ValidationError(UserIdField, SelfTransferMessage)]);

        if (!target.IsEmailVerified)
            throw new ForbiddenException(ForbiddenReason.EmailNotVerified);

        return target;
    }

    // El aviso de RF-77 para la organización y el destino ya validado. No modifica nada.
    public static async Task<OwnershipTransferPreviewDto> BuildAsync(
        IZentoryDbContext      db,
        IPlanResolutionService plans,
        IPlanLimitService      planLimits,
        OrganizationEntity     organization,
        Guid                   targetUserId,
        DateTime               nowUtc,
        CancellationToken      ct)
    {
        // Plan efectivo de hoy: el del owner (RF-73). Plan nuevo: el plan propio del destino, que pasará a sostenerla.
        var currentPlan = await plans.ResolveForOwnerAsync(organization.OwnerId, ct);
        var newPlan     = await plans.ResolveForOwnerAsync(targetUserId, ct);

        var lostFeatures   = await LostFeaturesAsync(db, organization.LegalType, currentPlan, newPlan, ct);
        var exceededLimits = await ExceededLimitsAsync(db, organization.OrganizationId, organization.LegalType, newPlan, nowUtc, ct);
        var blocked        = await ReachedOwnedOrganizationLimitAsync(db, planLimits, organization.LegalType, targetUserId, newPlan, ct);

        var hash = ComputeHash(
            organization.OrganizationId, targetUserId, currentPlan, newPlan, lostFeatures, exceededLimits, blocked);

        return new OwnershipTransferPreviewDto(
            currentPlan, newPlan, lostFeatures, exceededLimits, blocked, blocked ? QuotaExceededReason : null, hash);
    }

    // SHA-256 en hexadecimal minúscula (64 caracteres) de un JSON canónico (D-33): propiedades en orden ordinal,
    // lostFeatures ordenadas, exceededLimits ordenados por featureKey y Guid en formato "D". No es un control de seguridad
    // (SR-0018.46): es el acuse de que el owner vio estas condiciones, y la aceptación lo recalcula.
    public static string ComputeHash(
        Guid                          organizationId,
        Guid                          targetUserId,
        string                        currentPlan,
        string                        newPlan,
        IEnumerable<string>           lostFeatures,
        IEnumerable<ExceededLimitDto> exceededLimits,
        bool                          blocked)
    {
        var canonical = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["blocked"]        = blocked,
            ["currentPlan"]    = currentPlan,
            ["exceededLimits"] = exceededLimits
                .OrderBy(l => l.FeatureKey, StringComparer.Ordinal)
                .Select(l => new SortedDictionary<string, object>(StringComparer.Ordinal)
                {
                    ["featureKey"] = l.FeatureKey,
                    ["limit"]      = l.Limit,
                    ["usage"]      = l.Usage,
                })
                .ToList(),
            ["lostFeatures"]   = lostFeatures.Order(StringComparer.Ordinal).ToList(),
            ["newPlan"]        = newPlan,
            ["organizationId"] = organizationId.ToString("D"),
            ["targetUserId"]   = targetUserId.ToString("D"),
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    // El acuse coincide con el aviso vigente (RF-64 d). Sin acuse, no coincide.
    public static bool Acknowledges(string? acknowledgedPreviewHash, string previewHash)
        => !string.IsNullOrWhiteSpace(acknowledgedPreviewHash)
           && string.Equals(acknowledgedPreviewHash.Trim(), previewHash, StringComparison.OrdinalIgnoreCase);

    private static int Rank(string plan) => Array.IndexOf(PlanOrder, plan);

    // Funciones del catálogo (PlanFeature, plan × legalType) de los planes por encima del nuevo hasta el actual: cada plan
    // lista lo que agrega sobre el anterior ("Todo lo de Pro, más"). Orden del catálogo: plan y posición.
    private static async Task<IReadOnlyList<string>> LostFeaturesAsync(
        IZentoryDbContext db, string legalType, string currentPlan, string newPlan, CancellationToken ct)
    {
        var lostPlans = PlanOrder.Where(plan => Rank(plan) > Rank(newPlan) && Rank(plan) <= Rank(currentPlan)).ToList();
        if (lostPlans.Count == 0)
            return [];

        var rows = await db.PlanFeatures
            .Where(f => f.LegalType == legalType)
            .Join(db.BillingPlans, f => f.PlanId, p => p.Id, (f, p) => new { PlanName = p.Name, f.SortOrder, f.Text })
            .Where(x => lostPlans.Contains(x.PlanName))
            .ToListAsync(ct);

        return rows
            .OrderBy(x => Rank(x.PlanName))
            .ThenBy(x => x.SortOrder)
            .Select(x => x.Text)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // Límites cuantitativos del plan nuevo (PlanLimit, plan × legalType) que el uso actual de la organización ya supera.
    // Sin límite (null) o sin fila en el catálogo, no se supera nada.
    private static async Task<IReadOnlyList<ExceededLimitDto>> ExceededLimitsAsync(
        IZentoryDbContext db, Guid organizationId, string legalType, string newPlan, DateTime nowUtc, CancellationToken ct)
    {
        var limits = await db.PlanLimits
            .Where(l => l.LegalType == legalType && OrganizationLimitKeys.Contains(l.FeatureKey) && l.LimitValue != null)
            .Join(db.BillingPlans, l => l.PlanId, p => p.Id, (l, p) => new { PlanName = p.Name, l.FeatureKey, l.LimitValue })
            .Where(x => x.PlanName == newPlan)
            .ToListAsync(ct);

        var exceeded = new List<ExceededLimitDto>();
        foreach (var limit in limits.OrderBy(l => l.FeatureKey, StringComparer.Ordinal))
        {
            var usage = await UsageAsync(db, organizationId, limit.FeatureKey, nowUtc, ct);
            if (usage > limit.LimitValue!.Value)
                exceeded.Add(new ExceededLimitDto(limit.FeatureKey, usage, limit.LimitValue.Value));
        }
        return exceeded;
    }

    // Uso actual con la misma definición que el límite al crear: miembros vigentes más invitaciones pendientes (RF-49 e),
    // clientes, facturas del mes calendario, proyectos y colaboradores vigentes.
    //
    // Sin filtros globales y con los predicados explícitos (OrganizationId y el borrado lógico de cada entidad): el aviso
    // debe dar lo mismo, y por tanto el mismo previewHash, desde la sesión del owner (organización activa = la
    // transferida) y desde la del destino, que puede tener activa otra organización o ninguna (RF-83 a, c; T30c). Lista
    // blanca de IgnoreQueryFilters (ADR-0008 §3).
    private static async Task<int> UsageAsync(
        IZentoryDbContext db, Guid organizationId, string featureKey, DateTime nowUtc, CancellationToken ct)
    {
        switch (featureKey)
        {
            case PlanLimits.FeatureKeys.MaxOrgMembers:
                var members = await db.OrganizationMembers.IgnoreQueryFilters()
                    .CountAsync(m => m.OrganizationId == organizationId && m.DeletedAt == null, ct);
                var invitations = await db.OrganizationInvitations.IgnoreQueryFilters().CountAsync(i =>
                    i.OrganizationId == organizationId && i.DeletedAt == null
                    && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > nowUtc, ct);
                return members + invitations;
            case PlanLimits.FeatureKeys.MaxClients:
                return await db.Clients.IgnoreQueryFilters()
                    .CountAsync(c => c.OrganizationId == organizationId && !c.IsDeleted, ct);
            case PlanLimits.FeatureKeys.MaxInvoicesMonth:
                var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var nextMonth  = monthStart.AddMonths(1);
                return await db.Invoices.IgnoreQueryFilters().CountAsync(i =>
                    i.OrganizationId == organizationId && i.DeletedAt == null
                    && i.CreatedAt >= monthStart && i.CreatedAt < nextMonth, ct);
            case PlanLimits.FeatureKeys.MaxProjects:
                return await db.Projects.IgnoreQueryFilters()
                    .CountAsync(p => p.OrganizationId == organizationId && !p.IsDeleted, ct);
            case PlanLimits.FeatureKeys.MaxCollaborators:
                return await db.Collaborators.IgnoreQueryFilters()
                    .CountAsync(c => c.OrganizationId == organizationId && c.DeletedAt == null, ct);
            default:
                return 0;
        }
    }

    // RF-64 b: el destino ya alcanzó max_owned_orgs según su plan propio (RF-74 a), con el legalType de la organización,
    // como al crear o restaurar. El filtro global de Organization deja fuera las eliminadas. El conteo no sale de aquí.
    private static async Task<bool> ReachedOwnedOrganizationLimitAsync(
        IZentoryDbContext db, IPlanLimitService planLimits, string legalType, Guid targetUserId, string targetPlan,
        CancellationToken ct)
    {
        var limit = await planLimits.GetLimitAsync(targetPlan, legalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, ct);
        if (limit is not { } max)
            return false;

        var owned = await db.Organizations.CountAsync(o => o.OwnerId == targetUserId, ct);
        return owned >= max;
    }
}

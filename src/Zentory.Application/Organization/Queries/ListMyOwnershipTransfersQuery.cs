using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Queries;

// GET /me/ownership-transfers (PLAN-0018 RF-83 a; D-33; T30c): el aviso del destino. Acción de usuario
// ([SkipMembershipValidation]): sirve con cualquier organización activa, o sin ninguna, porque las propuestas se buscan
// por ToUserId == sub y no por la organización activa. Devuelve solo las pending, no vencidas y de organizaciones
// vigentes. Sin reautenticación ni cambios (RF-81).
public sealed record ListMyOwnershipTransfersQuery : IRequest<IReadOnlyList<MyOwnershipTransferDto>>;

public sealed record MyOwnershipTransferDto(
    Guid                     Id,
    Guid                     OrganizationId,
    string                   OrganizationName,
    string                   FromUserName,
    DateTime                 ExpiresAt,
    TargetTransferPreviewDto Preview);

// Lo que cambia para el destino (RF-83 a): el plan efectivo de hoy, su plan propio (que pasará a sostener la
// organización), las funciones que se pierden, los límites que la organización ya supera con ese plan y sus propias
// organizaciones vigentes frente a su límite, del que la aceptación consume una. Sin previewHash: el acuse es del owner.
public sealed record TargetTransferPreviewDto(
    string                          CurrentPlan,
    string                          NewPlan,
    IReadOnlyList<string>           LostFeatures,
    IReadOnlyList<ExceededLimitDto> ExceededLimits,
    OwnedOrganizationsDto           OwnedOrganizations);

// Organizaciones propias vigentes del destino y su límite (null = sin límite). Son datos del propio usuario que llama.
public sealed record OwnedOrganizationsDto(int Usage, int? Limit);

public sealed class ListMyOwnershipTransfersQueryHandler
    : IRequestHandler<ListMyOwnershipTransfersQuery, IReadOnlyList<MyOwnershipTransferDto>>
{
    private readonly IZentoryDbContext      _db;
    private readonly ITenantContext         _tenant;
    private readonly IPlanResolutionService _plans;
    private readonly IPlanLimitService      _planLimits;
    private readonly TimeProvider           _time;

    public ListMyOwnershipTransfersQueryHandler(
        IZentoryDbContext      db,
        ITenantContext         tenant,
        IPlanResolutionService plans,
        IPlanLimitService      planLimits,
        TimeProvider           time)
    {
        _db         = db;
        _tenant     = tenant;
        _plans      = plans;
        _planLimits = planLimits;
        _time       = time;
    }

    public async Task<IReadOnlyList<MyOwnershipTransferDto>> Handle(ListMyOwnershipTransfersQuery request, CancellationToken ct)
    {
        var userId = _tenant.UserId;
        var now    = _time.GetUtcNow().UtcDateTime;

        // Las vencidas no aparecen (RF-64 c2): se tratan como inexistentes; las cancela quien las detecte al escribir.
        var transfers = await OwnershipTransferInbox.PendingForTarget(_db, userId)
            .AsNoTracking()
            .Where(t => t.ExpiresAt > now)
            .OrderBy(t => t.ExpiresAt)
            .ToListAsync(ct);
        if (transfers.Count == 0)
            return [];

        // El filtro global de Organization deja fuera las eliminadas (RF-83 a).
        var organizationIds = transfers.Select(t => t.OrganizationId).Distinct().ToList();
        var organizations = await _db.Organizations.AsNoTracking()
            .Where(o => organizationIds.Contains(o.OrganizationId))
            .ToDictionaryAsync(o => o.OrganizationId, ct);

        var fromUserIds = transfers.Select(t => t.FromUserId).Distinct().ToList();
        var fromNames = await _db.Users.AsNoTracking()
            .Where(u => fromUserIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var ownedUsage = await _db.Organizations.CountAsync(o => o.OwnerId == userId, ct);

        var result = new List<MyOwnershipTransferDto>(transfers.Count);
        foreach (var transfer in transfers)
        {
            if (!organizations.TryGetValue(transfer.OrganizationId, out var organization))
                continue;

            var preview = await OwnershipTransferPreview.BuildAsync(
                _db, _plans, _planLimits, organization, userId, now, ct);
            var ownedLimit = await _planLimits.GetLimitAsync(
                preview.NewPlan, organization.LegalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, ct);

            result.Add(new MyOwnershipTransferDto(
                transfer.Id,
                organization.OrganizationId,
                organization.Name,
                fromNames.GetValueOrDefault(transfer.FromUserId, string.Empty),
                transfer.ExpiresAt,
                new TargetTransferPreviewDto(
                    preview.CurrentPlan,
                    preview.NewPlan,
                    preview.LostFeatures,
                    preview.ExceededLimits,
                    new OwnedOrganizationsDto(ownedUsage, ownedLimit))));
        }
        return result;
    }
}

// Lecturas del destino de la transferencia (PLAN-0018 RF-83 i; ADR-0008 §3; T30c). El destino actúa desde cualquier
// organización activa (o sin ninguna), así que el filtro de tenant ocultaría la propuesta y las membresías de la
// organización transferida. Son las dos únicas lecturas sin filtros de las acciones del destino, cada una en la lista
// blanca de IgnoreQueryFilters con su predicado explícito.
public static class OwnershipTransferInbox
{
    // Propuestas pending cuyo destino es el usuario que llama: predicado explícito ToUserId == sub (el sub del token,
    // nunca un valor del cliente) y Status == pending. La vigencia la comprueba quien llama con IsPendingAt.
    public static IQueryable<OrganizationOwnershipTransfer> PendingForTarget(IZentoryDbContext db, Guid targetUserId)
        => db.OrganizationOwnershipTransfers
            .IgnoreQueryFilters()
            .Where(t => t.ToUserId == targetUserId && t.Status == OwnershipTransferStatus.Pending);

    // Membresías vigentes (DeletedAt == null) de los usuarios dados en la organización de la propuesta, con seguimiento:
    // la aceptación cambia sus roles. Predicado explícito OrganizationId == la de la propuesta ya validada para el sub.
    public static Task<List<OrganizationMember>> ActiveMembershipsAsync(
        IZentoryDbContext db, Guid organizationId, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        => db.OrganizationMembers
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.DeletedAt == null && userIds.Contains(m.UserId))
            .ToListAsync(ct);

    // La propuesta pending y vigente con ese id cuyo destino es el que llama, sin seguimiento; si no, null (404 uniforme
    // de RF-83 b 1). Una pending vencida se trata como inexistente y pasa a cancelled al detectarse (RF-64 c2, D-33): si
    // otra operación la resolvió a la vez, el resultado es el mismo null.
    public static async Task<OrganizationOwnershipTransfer?> FindPendingAsync(
        IZentoryDbContext db, IUnitOfWork uow, Guid transferId, Guid targetUserId, DateTime nowUtc, CancellationToken ct)
    {
        var transfer = await PendingForTarget(db, targetUserId).AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null)
            return null;
        if (transfer.IsPendingAt(nowUtc))
            return transfer;

        var expired = await PendingForTarget(db, targetUserId).FirstOrDefaultAsync(t => t.Id == transferId, ct);
        if (expired is not null && expired.Cancel(nowUtc))
        {
            try
            {
                await uow.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Otra operación la resolvió entre medias: ya no está pending, que es lo que importa.
            }
        }
        return null;
    }
}

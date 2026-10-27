using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Application.Organization.Commands;

// POST /organizations/{id}/restore (PLAN-0018 RF-72 b, c, d; RF-74 a; SR-0018.25, SR-0018.27, SR-0018.35; D-30, D-45).
// Acción de usuario: no usa la organización activa (sirve también a la sesión sin organización, RF-85). El límite
// organization-restore (5/h) ya corrió en el controlador. Orden:
//   1. Una sola lectura restaurable (Id, OwnerId == sub, eliminada, en plazo, sin purga): cualquier otro caso es 404 con
//      el mismo cuerpo, antes de mirar la cuota, así un id ajeno nunca responde 422 (sin enumeración).
//   2. max_owned_orgs con el plan propio del que llama (ResolveForOwnerAsync), nunca el de la organización activa.
//   3. Efecto en un SaveChanges con el ConcurrencyStamp de la organización (pierde ante la purga: 404) y el
//      OwnershipStamp del usuario (otra creación o restauración simultánea: se vuelve a contar una vez).
public sealed record RestoreOrganizationCommand(Guid OrganizationId) : IRequest;

// 422 QUOTA_EXCEEDED de max_owned_orgs con el mensaje de restaurar (RF-72 c): mismo código y featureKey que crear.
public sealed class RestoreQuotaExceededException(int limit, int used)
    : QuotaExceededException(PlanLimits.FeatureKeys.MaxOwnedOrgs, limit, used)
{
    public const string RestoreMessage =
        "Alcanzaste el límite de organizaciones propias de tu plan. Para restaurar esta organización, elimina otra " +
        "organización o sube de plan.";

    public override string Message => RestoreMessage;
}

public sealed class RestoreOrganizationCommandHandler : IRequestHandler<RestoreOrganizationCommand>
{
    public const string ResourceType = "Organization";
    public const string ChangedCode    = "ORGANIZATION_CHANGED";
    public const string ChangedMessage = "La organización cambió mientras se restauraba. Vuelve a cargarla e inténtalo de nuevo.";

    public const string AuditTable  = "organizations";
    public const string AuditAction = "RESTORE";

    public const string ActivityEntityType = "Organization";
    public const string ActivityAction     = "Restauró la organización";

    // Un intento y un único reintento tras perder ante otra operación del mismo usuario sobre max_owned_orgs (D-45).
    private const int MaxAttempts = 2;

    private readonly IZentoryDbContext          _db;
    private readonly IUnitOfWork                _uow;
    private readonly ITenantContext             _tenant;
    private readonly IDeletedOrganizationReader _deleted;
    private readonly IPlanResolutionService     _plans;
    private readonly IPlanLimitService          _planLimits;

    public RestoreOrganizationCommandHandler(
        IZentoryDbContext          db,
        IUnitOfWork                uow,
        ITenantContext             tenant,
        IDeletedOrganizationReader deleted,
        IPlanResolutionService     plans,
        IPlanLimitService          planLimits)
    {
        _db         = db;
        _uow        = uow;
        _tenant     = tenant;
        _deleted    = deleted;
        _plans      = plans;
        _planLimits = planLimits;
    }

    public async Task Handle(RestoreOrganizationCommand request, CancellationToken ct)
    {
        var userId = _tenant.UserId;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RestoreAsync(request.OrganizationId, userId, ct);
                return;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                // La purga marcó PurgeStartedAt (o algo cambió la organización) después de leerla: ya no se restaura.
                if (exception.Entries.Any(entry => entry.Entity is OrganizationEntity))
                    throw new NotFoundException(ResourceType, request.OrganizationId);
                if (attempt >= MaxAttempts)
                    throw new ConflictException(ChangedCode, ChangedMessage);

                // Otra creación o restauración del mismo usuario ganó: se descarta lo pendiente y se vuelve a contar.
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }

    private async Task RestoreAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        // El usuario primero: su OwnershipStamp debe ser el leído antes de contar (D-45). Sin él, el mismo 404.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw new NotFoundException(ResourceType, organizationId);
        var organization = await _deleted.FindRestorableAsync(userId, organizationId, ct)
            ?? throw new NotFoundException(ResourceType, organizationId);

        await EnsureOwnedOrganizationQuotaAsync(organization, userId, ct);

        organization.Restore();
        user.TouchOwnership();

        _db.AuditLogs.Add(AuditLog.Record(
            tableName:      AuditTable,
            recordId:       organizationId,
            action:         AuditAction,
            organizationId: organizationId,
            userId:         userId,
            newValues:      JsonSerializer.Serialize(new { restoredBy = userId })));

        // ActivityLog solo en empresa (como IActivityLogService), pero con la organización restaurada y no la activa.
        if (string.Equals(organization.LegalType, LegalType.Empresa, StringComparison.OrdinalIgnoreCase))
            _db.ActivityLogs.Add(ActivityLog.Create(
                organizationId: organizationId,
                entityType:     ActivityEntityType,
                entityId:       organizationId,
                userInitials:   _tenant.UserInitials,
                action:         ActivityAction,
                entityCode:     organization.Name,
                userId:         userId));

        await _uow.SaveChangesAsync(ct);
    }

    // RF-72 c y RF-74 a: cuenta las organizaciones propias vigentes (el filtro global deja fuera las eliminadas) y la que
    // se restaura debe caber en el límite del plan propio.
    private async Task EnsureOwnedOrganizationQuotaAsync(OrganizationEntity organization, Guid userId, CancellationToken ct)
    {
        var plan  = await _plans.ResolveForOwnerAsync(userId, ct);
        var limit = await _planLimits.GetLimitAsync(plan, organization.LegalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, ct);
        if (limit is not { } max)
            return;

        var ownedCount = await _db.Organizations.CountAsync(o => o.OwnerId == userId, ct);
        if (ownedCount >= max)
            throw new RestoreQuotaExceededException(max, ownedCount);
    }
}

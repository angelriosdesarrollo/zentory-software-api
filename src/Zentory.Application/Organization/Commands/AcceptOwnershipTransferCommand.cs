using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.Queries;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Application.Organization.Commands;

// POST /ownership-transfers/{id}/accept (PLAN-0018 RF-83 b, c, d, e; RF-74 a; RF-81; SR-0018.37; D-33, D-45; T30c): el
// destino acepta la transferencia con su propia credencial. Acción de usuario ([SkipMembershipValidation]): sirve con
// cualquier organización activa. Orden de RF-83 b:
//   1. 404 uniforme (inexistente, de otro destino, vencida o resuelta), sin verificar la credencial. El controlador lo
//      comprueba antes del rate limit con EnsureOwnershipTransferTargetCommand, y aquí se repite por la carrera.
//   2. reauth:{sub} (5/15 min, compartido con eliminar y proponer) en el controlador.
//   3. Reautenticación del destino (acción organization.transfer.accept): 422 / 403 uniforme.
//   4. Revalidación: proponente sigue owner, destino miembro vigente y verificado, organización vigente (409
//      TRANSFER_INVALID); max_owned_orgs del destino con su plan propio (422 QUOTA_EXCEEDED); previewHash recalculado
//      igual al guardado (si no, la propuesta pasa a cancelled y 409 PREVIEW_STALE).
//   5. Efecto atómico en ExecuteInTransactionAsync con los tokens de concurrencia: OwnershipStamp del destino (D-45),
//      ConcurrencyStamp de la organización (SR-0018.37) y Status de la propuesta. Si otra operación ganó, 409 sin efecto.
// No escribe Subscriptions: el plan efectivo cambia porque cambia el owner (RF-73, CA-44). Sin ToString con valores: un
// record imprimiría la credencial si alguien lo registrara.
public sealed record AcceptOwnershipTransferCommand(Guid TransferId, string? CurrentPassword, string? GoogleIdToken) : IRequest
{
    public override string ToString() => nameof(AcceptOwnershipTransferCommand);
}

// Paso 1 de RF-83 b, antes del rate limit (filtro del controlador): la propuesta existe, es del que llama, sigue pending y
// no venció. Si no, el mismo 404 que el comando, sin contar intento ni verificar credencial.
public sealed record EnsureOwnershipTransferTargetCommand(Guid TransferId) : IRequest;

// 422 QUOTA_EXCEEDED de max_owned_orgs del destino al aceptar (RF-83 c, RF-74 a). El uso es el del propio usuario que
// llama (lo mismo que ve en ownedOrganizations del aviso), no de un tercero.
public sealed class AcceptOwnershipTransferQuotaExceededException(int limit, int used)
    : QuotaExceededException(PlanLimits.FeatureKeys.MaxOwnedOrgs, limit, used)
{
    public const string AcceptMessage =
        "Alcanzaste el límite de organizaciones propias de tu plan. Para aceptar esta organización, elimina otra " +
        "organización o sube de plan.";

    public override string Message => AcceptMessage;
}

public static class OwnershipTransferTarget
{
    public const string ResourceType = "OrganizationOwnershipTransfer";

    public const string InvalidCode    = "TRANSFER_INVALID";
    public const string InvalidMessage = "La transferencia ya no es válida.";
}

public sealed class EnsureOwnershipTransferTargetCommandHandler : IRequestHandler<EnsureOwnershipTransferTargetCommand>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;
    private readonly TimeProvider      _time;

    public EnsureOwnershipTransferTargetCommandHandler(
        IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant, TimeProvider time)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
        _time   = time;
    }

    public async Task Handle(EnsureOwnershipTransferTargetCommand request, CancellationToken ct)
    {
        _ = await OwnershipTransferInbox.FindPendingAsync(
                _db, _uow, request.TransferId, _tenant.UserId, _time.GetUtcNow().UtcDateTime, ct)
            ?? throw new NotFoundException(OwnershipTransferTarget.ResourceType, request.TransferId);
    }
}

public sealed class AcceptOwnershipTransferCommandHandler : IRequestHandler<AcceptOwnershipTransferCommand>
{
    public const string LogAction = "Aceptó la transferencia de la propiedad";

    // Un intento y un único reintento tras perder ante otra operación del destino sobre max_owned_orgs (D-45).
    private const int MaxAttempts = 2;

    private readonly IZentoryDbContext         _db;
    private readonly IUnitOfWork               _uow;
    private readonly ITenantContext            _tenant;
    private readonly IReauthenticationVerifier _reauth;
    private readonly IReauthNonceStore         _nonces;
    private readonly IPlanResolutionService    _plans;
    private readonly IPlanLimitService         _planLimits;
    private readonly IRefreshTokenRepository   _refreshTokens;
    private readonly TimeProvider              _time;

    public AcceptOwnershipTransferCommandHandler(
        IZentoryDbContext         db,
        IUnitOfWork               uow,
        ITenantContext            tenant,
        IReauthenticationVerifier reauth,
        IReauthNonceStore         nonces,
        IPlanResolutionService    plans,
        IPlanLimitService         planLimits,
        IRefreshTokenRepository   refreshTokens,
        TimeProvider              time)
    {
        _db            = db;
        _uow           = uow;
        _tenant        = tenant;
        _reauth        = reauth;
        _nonces        = nonces;
        _plans         = plans;
        _planLimits    = planLimits;
        _refreshTokens = refreshTokens;
        _time          = time;
    }

    public async Task Handle(AcceptOwnershipTransferCommand request, CancellationToken ct)
    {
        var userId = _tenant.UserId;
        var now    = _time.GetUtcNow().UtcDateTime;

        // 1. 404 uniforme, antes de la credencial (RF-83 b 1).
        var found = await OwnershipTransferInbox.FindPendingAsync(_db, _uow, request.TransferId, userId, now, ct)
            ?? throw NotFound(request.TransferId);

        // 3. Reautenticación del destino con su propia credencial (RF-81). Sin cambios pendientes en el contexto: el
        //    verificador guarda su AuditLog de fallo con SaveChanges sobre este mismo contexto. El usuario se carga
        //    primero y con seguimiento: su OwnershipStamp es el que se escribe (D-45).
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct) ?? throw NotFound(request.TransferId);
        var verification = await _reauth.VerifyAsync(
            user, new ReauthCredential(request.CurrentPassword, request.GoogleIdToken),
            ReauthAction.OrganizationTransferAccept, found.OrganizationId, ct);

        var consumed = false;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                // 4. Revalidación con lecturas posteriores a la credencial (otra operación pudo resolver la propuesta).
                var accepted = await PrepareAsync(request.TransferId, userId, now, ct);

                // Nonce de Google: un solo uso, consumido justo antes de escribir (RF-84 d). La contraseña no lo usa.
                if (!consumed)
                {
                    if (!verification.TryConsume(_nonces))
                        throw new ReauthenticationFailedException();
                    consumed = true;
                }

                try
                {
                    await WriteAsync(accepted, userId, now, ct);
                    return;
                }
                catch (DbUpdateConcurrencyException exception)
                    when (attempt < MaxAttempts && exception.Entries.All(entry => entry.Entity is User))
                {
                    // Otra creación, restauración o aceptación del destino ganó su OwnershipStamp: se descarta lo pendiente
                    // y se vuelve a contar una vez (D-45).
                    exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Aceptar, rechazar, cancelar, eliminar, cambiar el rol o dar de baja ganaron (SR-0018.37): sin efecto.
                    throw new ConflictException(OwnershipTransferTarget.InvalidCode, OwnershipTransferTarget.InvalidMessage);
                }
            }
        }
        catch
        {
            // El reintento legítimo con el mismo nonce debe volver a funcionar si la escritura no se confirmó.
            if (consumed)
                verification.Release(_nonces);
            throw;
        }
    }

    private sealed record Acceptance(
        User                          Target,
        OrganizationEntity            Organization,
        OrganizationOwnershipTransfer Transfer,
        OrganizationMember            TargetMembership,
        OrganizationMember            PreviousOwnerMembership);

    // Lecturas con seguimiento en este orden: destino, organización, propuesta y membresías. Las escrituras que compiten
    // (RF-83 e) chocan primero con los sellos del destino y de la organización.
    private async Task<Acceptance> PrepareAsync(Guid transferId, Guid userId, DateTime now, CancellationToken ct)
    {
        var target = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct) ?? throw NotFound(transferId);
        var snapshot = await OwnershipTransferInbox.FindPendingAsync(_db, _uow, transferId, userId, now, ct)
            ?? throw NotFound(transferId);

        // El filtro global de Organization deja fuera la eliminada: también es TRANSFER_INVALID (RF-83 c).
        var organization = await _db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == snapshot.OrganizationId, ct);
        if (organization is null || organization.OwnerId != snapshot.FromUserId)
            throw Invalid();

        var transfer = await OwnershipTransferInbox.PendingForTarget(_db, userId)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct) ?? throw NotFound(transferId);

        var memberships = await OwnershipTransferInbox.ActiveMembershipsAsync(
            _db, organization.OrganizationId, [userId, snapshot.FromUserId], ct);
        var targetMembership   = memberships.FirstOrDefault(m => m.UserId == userId);
        var previousMembership = memberships.FirstOrDefault(m => m.UserId == snapshot.FromUserId);
        if (targetMembership is null || previousMembership is null || !target.IsEmailVerified)
            throw Invalid();

        // max_owned_orgs con el plan propio del destino y el legalType de la organización (RF-74 a, D-30). El filtro
        // global de Organization deja fuera las eliminadas.
        var newPlan = await _plans.ResolveForOwnerAsync(userId, ct);
        if (await _planLimits.GetLimitAsync(newPlan, organization.LegalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, ct) is { } max)
        {
            var owned = await _db.Organizations.CountAsync(o => o.OwnerId == userId, ct);
            if (owned >= max)
                throw new AcceptOwnershipTransferQuotaExceededException(max, owned);
        }

        // El destino nunca acepta condiciones distintas de las que el owner acusó (RF-83 c; D-33): mismo cálculo, misma
        // función de hash.
        var preview = await OwnershipTransferPreview.BuildAsync(_db, _plans, _planLimits, organization, userId, now, ct);
        if (!string.Equals(preview.PreviewHash, transfer.PreviewHash, StringComparison.Ordinal))
        {
            await CancelStaleAsync(transfer, now, ct);
            throw new ConflictException(
                ProposeOwnershipTransferCommandHandler.PreviewStaleCode, ProposeOwnershipTransferCommandHandler.PreviewStaleMessage);
        }

        return new Acceptance(target, organization, transfer, targetMembership, previousMembership);
    }

    // La propuesta con el aviso desactualizado pasa a cancelled (el owner debe proponer de nuevo). Solo cambia la
    // propuesta: el destino, la organización y las membresías leídas siguen sin cambios.
    private async Task CancelStaleAsync(OrganizationOwnershipTransfer transfer, DateTime now, CancellationToken ct)
    {
        transfer.Cancel(now);
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra operación la resolvió a la vez: ya no está pending, y la respuesta sigue siendo PREVIEW_STALE.
        }
    }

    // Efecto de RF-83 e. Primero los cambios con token de concurrencia y los roles (un solo SaveChanges), después el
    // ActivityLog y la revocación de los refresh tokens de los dos, en la misma transacción. La invariante de RF-48 e
    // (exactamente un owner vigente, el de OwnerId) se cumple al confirmar.
    private Task WriteAsync(Acceptance accepted, Guid userId, DateTime now, CancellationToken ct)
        => _uow.ExecuteInTransactionAsync(async token =>
        {
            var previousOwnerId = accepted.Transfer.FromUserId;
            var targetRoleBefore = accepted.TargetMembership.Role;

            accepted.Target.TouchOwnership();
            accepted.Organization.SetOwner(userId);
            accepted.Transfer.Accept(now);
            accepted.TargetMembership.ChangeRole(OrgRole.Owner);
            accepted.TargetMembership.Touch();
            accepted.PreviousOwnerMembership.ChangeRole(OrgRole.Admin);
            accepted.PreviousOwnerMembership.Touch();
            await _uow.SaveChangesAsync(token);

            // ActivityLog de la organización transferida, no de la activa del token (como la aceptación de invitación).
            if (string.Equals(accepted.Organization.LegalType, LegalType.Empresa, StringComparison.OrdinalIgnoreCase))
            {
                _db.ActivityLogs.Add(ActivityLog.Create(
                    organizationId: accepted.Organization.OrganizationId,
                    entityType:     ProposeOwnershipTransferCommandHandler.EntityType,
                    entityId:       accepted.Transfer.Id,
                    userInitials:   _tenant.UserInitials,
                    action:         LogAction,
                    entityCode:     accepted.Organization.Name,
                    metadata:       JsonSerializer.Serialize(new
                    {
                        actorUserId   = userId,
                        fromUserId    = previousOwnerId,
                        toUserId      = userId,
                        ownerBefore   = previousOwnerId,
                        ownerAfter    = userId,
                        toRoleBefore  = targetRoleBefore,
                        toRoleAfter   = OrgRole.Owner,
                        fromRoleAfter = OrgRole.Admin,
                    }),
                    userId:         userId,
                    occurredAt:     now));
                await _uow.SaveChangesAsync(token);
            }

            // Los refresh tokens de los dos dejan de servir (RF-83 e). Sus access tokens toman los roles nuevos en la
            // siguiente petición porque el middleware los lee de la membresía (RF-14). No toca CurrentSessionId.
            await MemberManagement.RevokeRefreshTokensAsync(_refreshTokens, _uow, userId, token);
            await MemberManagement.RevokeRefreshTokensAsync(_refreshTokens, _uow, previousOwnerId, token);
        }, ct);

    private static NotFoundException NotFound(Guid transferId)
        => new(OwnershipTransferTarget.ResourceType, transferId);

    private static ConflictException Invalid()
        => new(OwnershipTransferTarget.InvalidCode, OwnershipTransferTarget.InvalidMessage);
}

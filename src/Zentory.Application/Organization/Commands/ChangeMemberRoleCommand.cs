using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Application.Organization.Commands;

// PATCH /organization/members/{userId}/role (PLAN-0018 RF-46 a, RF-48, RF-51, RF-87 b; D-19, D-21). La policy
// ManageMembers ya corrió en el controlador (empresa + plan efectivo pro + owner o admin); aquí van las reglas finas.
public sealed record ChangeMemberRoleCommand(Guid UserId, string? RoleId) : IRequest;

public sealed class ChangeMemberRoleCommandHandler : IRequestHandler<ChangeMemberRoleCommand>
{
    public const string RoleIdField   = "roleId";
    public const string RoleIdMessage = "El rol no se puede asignar.";
    public const string LogAction     = "Cambió el rol de un miembro";

    public const string ChangedCode    = "ORGANIZATION_CHANGED";
    public const string ChangedMessage = "La organización cambió mientras se cambiaba el rol. Vuelve a cargarla e inténtalo de nuevo.";

    private readonly IZentoryDbContext       _db;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IActivityLogService     _activityLog;
    private readonly TimeProvider            _time;

    public ChangeMemberRoleCommandHandler(
        IZentoryDbContext       db,
        IUnitOfWork             uow,
        ITenantContext          tenant,
        IRefreshTokenRepository refreshTokens,
        IActivityLogService     activityLog,
        TimeProvider            time)
    {
        _time          = time;
        _db            = db;
        _uow           = uow;
        _tenant        = tenant;
        _refreshTokens = refreshTokens;
        _activityLog   = activityLog;
    }

    public async Task Handle(ChangeMemberRoleCommand request, CancellationToken ct)
    {
        // RF-48 a: owner solo se obtiene creando la organización o por transferencia; cualquier otro valor tampoco.
        var roleId = request.RoleId;
        if (roleId is null || !OrgRole.Assignable.Contains(roleId, StringComparer.Ordinal))
            throw new ValidationException([new ValidationError(RoleIdField, RoleIdMessage)]);

        var (organization, membership) = await MemberManagement.TargetAsync(_db, _tenant, request.UserId, ct);
        var before = membership.Role;

        // Condicionado al sello leído (RF-83 e; T30c): si una transferencia aceptada entre medias hizo owner al objetivo,
        // el cambio no degrada al nuevo owner y responde 409 sin efecto.
        organization.TouchConcurrencyStamp();
        membership.ChangeRole(roleId);
        membership.Touch();
        // REV-0005 H-5: quien deja de ser owner o admin ya no puede sostener invitaciones pendientes (RF-48).
        if (roleId != OrgRole.Admin && roleId != OrgRole.Owner)
            await MemberManagement.RevokePendingInvitationsOfAsync(
                _db, membership.OrganizationId, request.UserId, _time.GetUtcNow().UtcDateTime, ct);
        await _activityLog.LogAsync(
            entityType: MemberManagement.EntityType,
            entityId:   membership.Id,
            action:     LogAction,
            metadata:   MemberManagement.Metadata(_tenant.UserId, request.UserId, before, roleId),
            ct:         ct);
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ChangedCode, ChangedMessage);
        }

        // RF-46: el refresh anterior del afectado deja de servir; su access token ya toma el rol nuevo en la siguiente
        // petición porque el middleware lo lee de la membresía (RF-14).
        await MemberManagement.RevokeRefreshTokensAsync(_refreshTokens, _uow, request.UserId, ct);
    }
}

// Reglas comunes al cambio de rol y a la baja (RF-46, RF-48 b y c, RF-87 b; D-19).
internal static class MemberManagement
{
    public const string EntityType = "OrganizationMember";

    private const int MaxRevokeAttempts = 3;

    // La organización activa (con seguimiento) y la membresía vigente del objetivo, con las protecciones aplicadas:
    // - owner por identidad (Organization.OwnerId), nunca por el rol guardado → 403 ACCESS_DENIED;
    // - sobre uno mismo → 403 ACCESS_DENIED;
    // - sin membresía vigente en la organización activa (p. ej. un miembro de otra organización) → 404 sin efecto.
    // La organización se carga primero y con seguimiento: quien escribe llama a TouchConcurrencyStamp, así el OwnerId
    // comprobado aquí es el del sello que se escribe (T30c, carrera con la aceptación de la transferencia).
    public static async Task<(OrganizationEntity Organization, OrganizationMember Membership)> TargetAsync(
        IZentoryDbContext db, ITenantContext tenant, Guid targetUserId, CancellationToken ct)
    {
        var organizationId = tenant.OrganizationId;
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("OrganizationMember", targetUserId);

        if (targetUserId == organization.OwnerId || targetUserId == tenant.UserId)
            throw new ForbiddenException(ForbiddenReason.TenantMismatch);

        var membership = await db.OrganizationMembers
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == targetUserId, ct)
            ?? throw new NotFoundException("OrganizationMember", targetUserId);
        return (organization, membership);
    }

    // REV-0005 H-5: las invitaciones pendientes de la organización cuyo emisor es el afectado se revocan en la misma
    // escritura que su baja o su paso a un rol sin permiso de invitar; las de otros emisores no se tocan.
    public static async Task RevokePendingInvitationsOfAsync(
        IZentoryDbContext db, Guid organizationId, Guid issuerUserId, DateTime nowUtc, CancellationToken ct)
    {
        var pending = await db.OrganizationInvitations
            .Where(i => i.OrganizationId == organizationId
                     && i.InvitedBy == issuerUserId
                     && i.AcceptedAt == null
                     && i.RevokedAt == null
                     && i.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var invitation in pending)
            invitation.Revoke(nowUtc);
    }

    // Metadata de ActivityLog (RF-51): actor, objetivo, valor anterior y valor nuevo (null en la baja).
    public static string Metadata(Guid actorUserId, Guid targetUserId, string before, string? after)
        => JsonSerializer.Serialize(new { actorUserId, targetUserId, before, after });

    // Revoca todos los refresh tokens del afectado después de confirmar el cambio: un refresh emitido entre medias ya
    // trae el estado nuevo y también queda revocado. Si una rotación simultánea revocó alguno (token de concurrencia
    // IsRevoked), se descarta lo pendiente y se vuelve a leer, como en RefreshTokenCommand.
    public static async Task RevokeRefreshTokensAsync(
        IRefreshTokenRepository refreshTokens, IUnitOfWork uow, Guid userId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await refreshTokens.RevokeAllForUserAsync(userId, ct);
                await uow.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < MaxRevokeAttempts)
            {
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

// DELETE /organization/members/{userId} (PLAN-0018 RF-46 b, RF-48, RF-51, RF-83 h, RF-87 b; D-19, D-21): borrado lógico
// de la membresía. La policy ManageMembers ya corrió en el controlador; las protecciones del owner y de uno mismo, y el
// 404 de un userId sin membresía en la organización activa, son las de MemberManagement. Si el removido era el destino de
// la transferencia pendiente, la propuesta queda cancelada en la misma escritura (RF-83 h).
public sealed record RemoveMemberCommand(Guid UserId) : IRequest;

public sealed class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand>
{
    public const string LogAction = "Dio de baja a un miembro";

    public const string ChangedCode    = "ORGANIZATION_CHANGED";
    public const string ChangedMessage = "La organización cambió mientras se daba de baja al miembro. Vuelve a cargarla e inténtalo de nuevo.";

    private readonly IZentoryDbContext       _db;
    private readonly IUnitOfWork             _uow;
    private readonly ITenantContext          _tenant;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IActivityLogService     _activityLog;
    private readonly TimeProvider            _time;

    public RemoveMemberCommandHandler(
        IZentoryDbContext       db,
        IUnitOfWork             uow,
        ITenantContext          tenant,
        IRefreshTokenRepository refreshTokens,
        IActivityLogService     activityLog,
        TimeProvider            time)
    {
        _db            = db;
        _uow           = uow;
        _tenant        = tenant;
        _refreshTokens = refreshTokens;
        _activityLog   = activityLog;
        _time          = time;
    }

    public async Task Handle(RemoveMemberCommand request, CancellationToken ct)
    {
        var (organization, membership) = await MemberManagement.TargetAsync(_db, _tenant, request.UserId, ct);

        // Condicionado al sello leído (RF-83 e; T30c): si una transferencia aceptada entre medias hizo owner al removido,
        // la baja no tiene efecto (409 de abajo).
        organization.TouchConcurrencyStamp();
        membership.SoftDelete();
        await CancelTransfersToAsync(membership.OrganizationId, request.UserId, ct);
        // REV-0005 H-5: las invitaciones pendientes que emitió el removido dejan de ser canjeables.
        await MemberManagement.RevokePendingInvitationsOfAsync(
            _db, membership.OrganizationId, request.UserId, _time.GetUtcNow().UtcDateTime, ct);
        await _activityLog.LogAsync(
            entityType: MemberManagement.EntityType,
            entityId:   membership.Id,
            action:     LogAction,
            metadata:   MemberManagement.Metadata(_tenant.UserId, request.UserId, membership.Role, after: null),
            ct:         ct);
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // La propuesta hacia el removido se resolvió a la vez, o la organización cambió de owner (la aceptó, T30c): la
            // baja no tiene efecto.
            throw new ConflictException(ChangedCode, ChangedMessage);
        }

        // El access token vigente ya recibe 401 MEMBERSHIP_REVOKED en la siguiente petición (RF-14); el refresh deja de
        // servir aquí.
        await MemberManagement.RevokeRefreshTokensAsync(_refreshTokens, _uow, request.UserId, ct);
    }

    // RF-83 h: la baja del destino cancela la propuesta pendiente (vigente o vencida). Status es token de concurrencia, así
    // que una aceptación simultánea y esta baja no pueden confirmar las dos.
    private async Task CancelTransfersToAsync(Guid organizationId, Guid targetUserId, CancellationToken ct)
    {
        var pending = await _db.OrganizationOwnershipTransfers
            .Where(t => t.OrganizationId == organizationId
                     && t.ToUserId == targetUserId
                     && t.Status == OwnershipTransferStatus.Pending)
            .ToListAsync(ct);

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var transfer in pending)
            transfer.Cancel(now);
    }
}

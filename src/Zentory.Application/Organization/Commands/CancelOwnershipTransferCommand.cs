using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

// DELETE /organization/transfer-ownership (PLAN-0018 RF-64 c3; D-33): el owner cancela la propuesta pendiente de la
// organización activa. La policy RequireOrgOwner ya corrió en el controlador (admin y member: 403 ACCESS_DENIED); no pide
// reautenticación (RF-81). Idempotente: sin propuesta pendiente responde igual, sin efecto ni ActivityLog.
public sealed record CancelOwnershipTransferCommand : IRequest;

public sealed class CancelOwnershipTransferCommandHandler : IRequestHandler<CancelOwnershipTransferCommand>
{
    public const string LogAction = "Canceló la transferencia de la propiedad";

    private readonly IZentoryDbContext   _db;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;
    private readonly TimeProvider        _time;

    public CancelOwnershipTransferCommandHandler(
        IZentoryDbContext   db,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog,
        TimeProvider        time)
    {
        _db          = db;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
        _time        = time;
    }

    public async Task Handle(CancelOwnershipTransferCommand request, CancellationToken ct)
    {
        var organizationId = _tenant.OrganizationId;
        var pending = await _db.OrganizationOwnershipTransfers
            .Where(t => t.OrganizationId == organizationId && t.Status == OwnershipTransferStatus.Pending)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return;

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var transfer in pending)
        {
            transfer.Cancel(now);
            await _activityLog.LogAsync(
                entityType: ProposeOwnershipTransferCommandHandler.EntityType,
                entityId:   transfer.Id,
                action:     LogAction,
                metadata:   JsonSerializer.Serialize(new { fromUserId = transfer.FromUserId, toUserId = transfer.ToUserId }),
                ct:         ct);
        }

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Status es token de concurrencia: otra operación resolvió la propuesta a la vez (aceptar, rechazar, cancelar o
            // eliminar la organización). Ya no está pendiente, que es lo que pide cancelar: mismo 204, sin efecto propio.
        }
    }
}

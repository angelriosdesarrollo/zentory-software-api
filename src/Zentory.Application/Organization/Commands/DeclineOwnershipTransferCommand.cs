using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.Queries;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Organization.Commands;

// POST /ownership-transfers/{id}/decline (PLAN-0018 RF-83 f; D-33; T30c): el destino rechaza la propuesta. Acción de
// usuario ([SkipMembershipValidation]) sin reautenticación (RF-81). El mismo 404 uniforme que aceptar (inexistente, de
// otro destino, vencida o resuelta); una vencida pasa a cancelled al detectarse. Solo cambia la propuesta (declined) y
// deja ActivityLog en la organización de la propuesta.
public sealed record DeclineOwnershipTransferCommand(Guid TransferId) : IRequest;

public sealed class DeclineOwnershipTransferCommandHandler : IRequestHandler<DeclineOwnershipTransferCommand>
{
    public const string LogAction = "Rechazó la transferencia de la propiedad";

    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;
    private readonly TimeProvider      _time;

    public DeclineOwnershipTransferCommandHandler(
        IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant, TimeProvider time)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
        _time   = time;
    }

    public async Task Handle(DeclineOwnershipTransferCommand request, CancellationToken ct)
    {
        var userId = _tenant.UserId;
        var now    = _time.GetUtcNow().UtcDateTime;

        var snapshot = await OwnershipTransferInbox.FindPendingAsync(_db, _uow, request.TransferId, userId, now, ct)
            ?? throw NotFound(request.TransferId);
        var organization = await _db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrganizationId == snapshot.OrganizationId, ct);
        var transfer = await OwnershipTransferInbox.PendingForTarget(_db, userId)
            .FirstOrDefaultAsync(t => t.Id == request.TransferId, ct) ?? throw NotFound(request.TransferId);

        try
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                // Status es token de concurrencia: si aceptar, cancelar o eliminar ganaron, esta escritura pierde.
                transfer.Decline(now);
                await _uow.SaveChangesAsync(token);

                // ActivityLog de la organización de la propuesta, no de la activa del token (como aceptar).
                if (organization is not null
                    && string.Equals(organization.LegalType, LegalType.Empresa, StringComparison.OrdinalIgnoreCase))
                {
                    _db.ActivityLogs.Add(ActivityLog.Create(
                        organizationId: transfer.OrganizationId,
                        entityType:     ProposeOwnershipTransferCommandHandler.EntityType,
                        entityId:       transfer.Id,
                        userInitials:   _tenant.UserInitials,
                        action:         LogAction,
                        entityCode:     organization.Name,
                        metadata:       JsonSerializer.Serialize(new
                        {
                            actorUserId = userId,
                            fromUserId  = transfer.FromUserId,
                            toUserId    = transfer.ToUserId,
                            before      = OwnershipTransferStatus.Pending,
                            after       = OwnershipTransferStatus.Declined,
                        }),
                        userId:         userId,
                        occurredAt:     now));
                    await _uow.SaveChangesAsync(token);
                }
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otra operación resolvió la propuesta a la vez: ya no está pending, el mismo 404 uniforme.
            throw NotFound(request.TransferId);
        }
    }

    private static NotFoundException NotFound(Guid transferId)
        => new(OwnershipTransferTarget.ResourceType, transferId);
}

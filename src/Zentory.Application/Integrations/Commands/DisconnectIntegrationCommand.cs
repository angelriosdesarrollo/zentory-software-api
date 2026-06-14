using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Integrations.Commands;

public record DisconnectIntegrationCommand(string IntegrationId) : IRequest;

public sealed class DisconnectIntegrationCommandHandler : IRequestHandler<DisconnectIntegrationCommand>
{
    private readonly IZentoryDbContext _db;
    private readonly IUnitOfWork       _uow;
    private readonly ITenantContext    _tenant;

    public DisconnectIntegrationCommandHandler(IZentoryDbContext db, IUnitOfWork uow, ITenantContext tenant)
    {
        _db     = db;
        _uow    = uow;
        _tenant = tenant;
    }

    public async Task Handle(DisconnectIntegrationCommand request, CancellationToken cancellationToken)
    {
        var connection = await _db.OrganizationIntegrations
            .FirstOrDefaultAsync(
                oi => oi.OrganizationId == _tenant.OrganizationId && oi.IntegrationId == request.IntegrationId,
                cancellationToken);

        if (connection is null || !connection.IsActive)
            throw new NotFoundException("OrganizationIntegration", request.IntegrationId);

        connection.Disconnect();
        await _uow.SaveChangesAsync(cancellationToken);
    }
}

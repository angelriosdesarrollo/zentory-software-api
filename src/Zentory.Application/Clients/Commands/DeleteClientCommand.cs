using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Clients.Commands;

public record DeleteClientCommand(Guid Id) : IRequest;

public sealed class DeleteClientCommandHandler : IRequestHandler<DeleteClientCommand>
{
    private readonly IClientRepository   _clients;
    private readonly IUnitOfWork         _uow;
    private readonly ITenantContext      _tenant;
    private readonly IActivityLogService _activityLog;

    public DeleteClientCommandHandler(
        IClientRepository   clients,
        IUnitOfWork         uow,
        ITenantContext      tenant,
        IActivityLogService activityLog)
    {
        _clients     = clients;
        _uow         = uow;
        _tenant      = tenant;
        _activityLog = activityLog;
    }

    public async Task Handle(DeleteClientCommand request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(request.Id, cancellationToken);

        if (client is null || client.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Client", request.Id);

        var hasActiveProjects = await _clients.HasActiveProjectsOrPendingInvoicesAsync(request.Id, cancellationToken);
        if (hasActiveProjects)
            throw new ConflictException(
                "CLIENT_HAS_ACTIVE_PROJECTS",
                "El cliente tiene proyectos activos y no puede ser eliminado.");

        client.SoftDelete();

        await _clients.UpdateAsync(client, cancellationToken);

        await _activityLog.LogAsync(
            "Client",
            client.Id,
            $"Eliminó el cliente",
            client.Name,
            ct: cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);
    }
}

using MediatR;
using Zentory.Application.Clients.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Clients.Queries;

public record GetClientByIdQuery(Guid Id) : IRequest<ClientDto>;

public sealed class GetClientByIdQueryHandler : IRequestHandler<GetClientByIdQuery, ClientDto>
{
    private readonly IClientRepository _clients;
    private readonly ITenantContext    _tenant;

    public GetClientByIdQueryHandler(IClientRepository clients, ITenantContext tenant)
    {
        _clients = clients;
        _tenant  = tenant;
    }

    public async Task<ClientDto> Handle(GetClientByIdQuery request, CancellationToken cancellationToken)
    {
        var client = await _clients.GetByIdAsync(request.Id, cancellationToken);

        if (client is null || client.OrganizationId != _tenant.OrganizationId)
            throw new NotFoundException("Client", request.Id);

        return new ClientDto(
            client.Id,
            client.Name,
            client.ContactName,
            client.Email,
            client.Phone,
            client.City,
            client.Nit,
            client.Notes,
            ActiveProjects: 0,   // TODO: join with projects when available
            TotalBilled:    0m); // TODO: join with invoices when available
    }
}

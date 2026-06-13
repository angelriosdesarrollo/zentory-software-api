using MediatR;
using Zentory.Application.Clients.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Clients.Queries;

public record GetClientsQuery(string? Search = null)
    : IRequest<IReadOnlyList<ClientSummaryDto>>;

public sealed class GetClientsQueryHandler
    : IRequestHandler<GetClientsQuery, IReadOnlyList<ClientSummaryDto>>
{
    private readonly IClientRepository _clients;
    private readonly ITenantContext    _tenant;

    public GetClientsQueryHandler(IClientRepository clients, ITenantContext tenant)
    {
        _clients = clients;
        _tenant  = tenant;
    }

    public async Task<IReadOnlyList<ClientSummaryDto>> Handle(
        GetClientsQuery   request,
        CancellationToken cancellationToken)
    {
        var list = await _clients.ListAsync(_tenant.OrganizationId, request.Search, cancellationToken);

        return list.Select(c => new ClientSummaryDto(
            c.Id,
            c.Name,
            c.ContactName,
            c.Email,
            c.City,
            ActiveProjects: 0,  // TODO: join with projects when available
            TotalBilled:    0m  // TODO: join with invoices when available
        )).ToList();
    }
}

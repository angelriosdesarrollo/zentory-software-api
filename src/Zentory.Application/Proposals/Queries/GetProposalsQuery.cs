using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Proposals.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Queries;

public record GetProposalsQuery(
    string? Status   = null,
    Guid?   ClientId = null,
    string? Search   = null) : IRequest<IReadOnlyList<ProposalSummaryDto>>;

public sealed class GetProposalsQueryHandler
    : IRequestHandler<GetProposalsQuery, IReadOnlyList<ProposalSummaryDto>>
{
    private readonly IProposalRepository _proposals;
    private readonly IClientRepository   _clients;
    private readonly ITenantContext      _tenant;

    public GetProposalsQueryHandler(
        IProposalRepository proposals,
        IClientRepository   clients,
        ITenantContext      tenant)
    {
        _proposals = proposals;
        _clients   = clients;
        _tenant    = tenant;
    }

    public async Task<IReadOnlyList<ProposalSummaryDto>> Handle(
        GetProposalsQuery request,
        CancellationToken cancellationToken)
    {
        var list = await _proposals.ListAsync(
            _tenant.OrganizationId,
            request.Status,
            request.ClientId,
            request.Search,
            cancellationToken);

        var clientList = await _clients.ListAsync(_tenant.OrganizationId, ct: cancellationToken);
        var clientMap  = clientList.ToDictionary(c => c.Id, c => c.Name);

        return list.Select(p => new ProposalSummaryDto(
            p.Id,
            p.Title,
            p.ClientId,
            clientMap.GetValueOrDefault(p.ClientId, string.Empty),
            p.Status,
            p.TotalAmount,
            p.Currency,
            p.ExpiresAt,
            p.SentAt,
            p.ViewCount,
            p.CreatedAt)).ToList();
    }
}

using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Proposals.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Queries;

public record GetProposalByIdQuery(Guid Id) : IRequest<ProposalDto>;

public sealed class GetProposalByIdQueryHandler : IRequestHandler<GetProposalByIdQuery, ProposalDto>
{
    private readonly IProposalRepository _proposals;
    private readonly IClientRepository   _clients;
    private readonly ITenantContext      _tenant;

    public GetProposalByIdQueryHandler(
        IProposalRepository proposals,
        IClientRepository   clients,
        ITenantContext      tenant)
    {
        _proposals = proposals;
        _clients   = clients;
        _tenant    = tenant;
    }

    public async Task<ProposalDto> Handle(GetProposalByIdQuery request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (proposal is null || proposal.OrganizationId != _tenant.OrganizationId || proposal.DeletedAt.HasValue)
            throw new NotFoundException("Proposal", request.Id);

        var client = await _clients.GetByIdAsync(proposal.ClientId, cancellationToken);
        var items  = proposal.Items.Select(i => new ProposalItemDto(
            i.Id, i.Description, i.Quantity, i.UnitPrice, i.DiscountPct, i.Total, i.SortOrder)).ToList();

        return new ProposalDto(
            proposal.Id,
            proposal.Title,
            proposal.ClientId,
            client?.Name ?? string.Empty,
            proposal.Status,
            proposal.TotalAmount,
            proposal.Currency,
            proposal.IntroText,
            proposal.ExpiresAt,
            proposal.SentAt,
            proposal.AcceptedAt,
            proposal.RejectedAt,
            proposal.ViewCount,
            proposal.ConvertedToProjectId,
            items,
            proposal.CreatedAt,
            proposal.UpdatedAt);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Proposals.DTOs;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Proposals.Queries;

public record GetProposalByTokenQuery(Guid PublicToken) : IRequest<PublicProposalDto>;

public sealed class GetProposalByTokenQueryHandler : IRequestHandler<GetProposalByTokenQuery, PublicProposalDto>
{
    private readonly IProposalRepository _proposals;
    private readonly IClientRepository   _clients;
    private readonly IZentoryDbContext   _db;

    public GetProposalByTokenQueryHandler(
        IProposalRepository proposals,
        IClientRepository   clients,
        IZentoryDbContext   db)
    {
        _proposals = proposals;
        _clients   = clients;
        _db        = db;
    }

    public async Task<PublicProposalDto> Handle(GetProposalByTokenQuery request, CancellationToken cancellationToken)
    {
        var proposal = await _proposals.GetByPublicTokenAsync(request.PublicToken, cancellationToken);

        if (proposal is null)
            throw new NotFoundException("Proposal", request.PublicToken);

        var client = await _clients.GetByIdAsync(proposal.ClientId, cancellationToken);

        // Load sender identity from organization profile settings
        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.OrganizationId == proposal.OrganizationId, cancellationToken);

        var logoSetting = await _db.OrganizationSettings
            .FirstOrDefaultAsync(
                s => s.OrganizationId == proposal.OrganizationId && s.Key == "profile.logo_url",
                cancellationToken);

        var legalNameSetting = await _db.OrganizationSettings
            .FirstOrDefaultAsync(
                s => s.OrganizationId == proposal.OrganizationId && s.Key == "profile.legal_name",
                cancellationToken);

        var organizationName    = legalNameSetting?.Value ?? org?.Name ?? string.Empty;
        var organizationLogoUrl = logoSetting?.Value;

        var sections = proposal.Sections
            .Where(s => s.IsVisible)
            .OrderBy(s => s.SortOrder)
            .Select(s => new PublicSectionDto(s.SectionType, s.Title, s.Content, s.SortOrder, s.IsVisible))
            .ToList();

        var items = proposal.Items
            .OrderBy(i => i.SortOrder)
            .Select(i => new PublicItemDto(i.Description, i.Quantity, i.UnitPrice, i.DiscountPct, i.Total, i.SortOrder))
            .ToList();

        return new PublicProposalDto(
            proposal.Id,
            proposal.Title,
            proposal.Status,
            client?.Name ?? string.Empty,
            organizationName,
            organizationLogoUrl,
            proposal.Currency,
            proposal.TotalAmount,
            proposal.SentAt,
            proposal.ExpiresAt,
            proposal.AcceptedAt,
            proposal.RejectedAt,
            proposal.ViewCount,
            proposal.LastViewedAt,
            sections,
            items);
    }
}

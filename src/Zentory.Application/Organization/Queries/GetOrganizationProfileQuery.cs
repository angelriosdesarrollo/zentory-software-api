using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.DTOs;

namespace Zentory.Application.Organization.Queries;

public record GetOrganizationProfileQuery : IRequest<OrganizationProfileDto>;

public sealed class GetOrganizationProfileQueryHandler
    : IRequestHandler<GetOrganizationProfileQuery, OrganizationProfileDto>
{
    private readonly IZentoryDbContext      _db;
    private readonly ITenantContext         _tenant;
    private readonly IPlanResolutionService _plans;

    public GetOrganizationProfileQueryHandler(
        IZentoryDbContext db, ITenantContext tenant, IPlanResolutionService plans)
    {
        _db     = db;
        _tenant = tenant;
        _plans  = plans;
    }

    public async Task<OrganizationProfileDto> Handle(
        GetOrganizationProfileQuery request,
        CancellationToken           cancellationToken)
    {
        var org = await _db.Organizations
            .FirstOrDefaultAsync(o => o.OrganizationId == _tenant.OrganizationId, cancellationToken);

        if (org is null)
            throw new NotFoundException("Organization", _tenant.OrganizationId);

        // Profile fields are stored as org settings under dedicated keys
        var settings = await _db.OrganizationSettings
            .Where(s => s.OrganizationId == _tenant.OrganizationId)
            .ToListAsync(cancellationToken);

        var s    = settings.ToDictionary(x => x.Key, x => x.Value);
        var plan = await _plans.ResolveForOwnerAsync(org.OwnerId, cancellationToken);

        return new OrganizationProfileDto(
            Id:          org.OrganizationId,
            Name:        org.Name,
            Plan:        plan,
            AccountType: org.AccountType,
            Country:     org.Country,
            LegalName:   s.GetValueOrDefault("profile.legal_name"),
            LogoUrl:     s.GetValueOrDefault("profile.logo_url"),
            Nit:         s.GetValueOrDefault("profile.nit"),
            CompanyType: s.GetValueOrDefault("profile.company_type"),
            LegalRep:    s.GetValueOrDefault("profile.legal_rep"),
            LegalRepId:  s.GetValueOrDefault("profile.legal_rep_id"),
            TaxRegime:   s.GetValueOrDefault("profile.tax_regime"),
            Ciiu:        s.GetValueOrDefault("profile.ciiu"),
            Email:       s.GetValueOrDefault("profile.email"),
            Phone:       s.GetValueOrDefault("profile.phone"),
            Address:     s.GetValueOrDefault("profile.address"),
            City:        s.GetValueOrDefault("profile.city"));
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Auth.Queries;

public record ListUserOrganizationsQuery : IRequest<IReadOnlyList<OrgMembershipDto>>;

public sealed class ListUserOrganizationsQueryHandler
    : IRequestHandler<ListUserOrganizationsQuery, IReadOnlyList<OrgMembershipDto>>
{
    private readonly IZentoryDbContext      _db;
    private readonly ITenantContext         _tenant;
    private readonly IPlanResolutionService _plans;

    public ListUserOrganizationsQueryHandler(
        IZentoryDbContext db, ITenantContext tenant, IPlanResolutionService plans)
    {
        _db     = db;
        _tenant = tenant;
        _plans  = plans;
    }

    public async Task<IReadOnlyList<OrgMembershipDto>> Handle(
        ListUserOrganizationsQuery request, CancellationToken ct)
    {
        var rows = await _db.OrganizationMembers
            .Where(m => m.UserId == _tenant.UserId && m.DeletedAt == null)
            .Join(_db.Organizations,
                m => m.OrganizationId,
                o => o.OrganizationId,
                (m, o) => new { o.OrganizationId, o.Name, o.AccountType, o.OwnerId, m.Role, m.JoinedAt })
            .ToListAsync(ct);

        var plansByOwner = await _plans.ResolveForOwnersAsync(
            rows.Where(r => r.OwnerId.HasValue).Select(r => r.OwnerId!.Value), ct);

        return rows
            .Select(r => new OrgMembershipDto(
                r.OrganizationId.ToString(),
                r.Name,
                r.AccountType,
                r.OwnerId.HasValue ? plansByOwner.GetValueOrDefault(r.OwnerId.Value, Zentory.Domain.Constants.Plan.Free) : Zentory.Domain.Constants.Plan.Free,
                r.Role,
                r.JoinedAt.ToString("O")))
            .ToList();
    }
}

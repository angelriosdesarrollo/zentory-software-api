using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Auth.Queries;

public record ListUserOrganizationsQuery : IRequest<IReadOnlyList<OrgMembershipDto>>;

// Lista todas las organizaciones del usuario, no solo la activa: pasa por IUserMembershipReader porque el filtro de
// tenant de OrganizationMember solo deja ver la organización del token (PLAN-0018 RF-12).
public sealed class ListUserOrganizationsQueryHandler
    : IRequestHandler<ListUserOrganizationsQuery, IReadOnlyList<OrgMembershipDto>>
{
    private readonly IUserMembershipReader _memberships;
    private readonly ITenantContext        _tenant;

    public ListUserOrganizationsQueryHandler(IUserMembershipReader memberships, ITenantContext tenant)
    {
        _memberships = memberships;
        _tenant      = tenant;
    }

    public async Task<IReadOnlyList<OrgMembershipDto>> Handle(
        ListUserOrganizationsQuery request, CancellationToken ct)
    {
        var memberships = await _memberships.ListActiveAsync(_tenant.UserId, ct);
        return memberships.Select(m => m.ToDto()).ToList();
    }
}

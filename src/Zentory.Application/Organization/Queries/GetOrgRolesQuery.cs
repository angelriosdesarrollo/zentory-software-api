using MediatR;
using Zentory.Application.Organization.DTOs;
using Zentory.Domain.Constants;

namespace Zentory.Application.Organization.Queries;

// GET /organization/roles (PLAN-0018 RF-45 a; D-18): el catálogo fijo owner, admin y member. No lee la base: los roles
// no son configurables por organización.
public record GetOrgRolesQuery : IRequest<IReadOnlyList<OrgRoleDto>>;

public sealed class GetOrgRolesQueryHandler : IRequestHandler<GetOrgRolesQuery, IReadOnlyList<OrgRoleDto>>
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [OrgRole.Owner]  = "Propietario",
        [OrgRole.Admin]  = "Administrador",
        [OrgRole.Member] = "Miembro",
    };

    private static readonly IReadOnlyList<OrgRoleDto> Catalog = OrgRole.All
        .Select(role => new OrgRoleDto(role, Names[role], IsDefault: true, BadgeBg: null, BadgeColor: null))
        .ToList();

    public Task<IReadOnlyList<OrgRoleDto>> Handle(GetOrgRolesQuery request, CancellationToken cancellationToken)
        => Task.FromResult(Catalog);
}

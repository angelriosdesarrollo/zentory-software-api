using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Queries;

public record GetProfileQuery : IRequest<UserProfileDto>;

public sealed class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto>
{
    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly ITenantContext          _tenant;
    private readonly IPlanResolutionService  _plans;

    public GetProfileQueryHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        ITenantContext          tenant,
        IPlanResolutionService  plans)
    {
        _users         = users;
        _organizations = organizations;
        _tenant        = tenant;
        _plans         = plans;
    }

    public async Task<UserProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(_tenant.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", _tenant.UserId);

        var org = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);
        if (org is null)
            throw new NotFoundException("Organization", _tenant.OrganizationId);

        var plan = await _plans.ResolveForOwnerAsync(org.OwnerId, cancellationToken);

        return new UserProfileDto(
            user.UserId,
            user.FirstName,
            user.LastName,
            user.Email,
            plan,
            org.LegalType,
            user.Role,
            ActiveOrgId:          _tenant.OrganizationId.ToString(),
            ActiveOrgName:        org.Name,
            ActiveOrgRole:        _tenant.ActiveOrgRole,
            TermsAcceptedAt:      user.TermsAcceptedAt,
            TermsAcceptedVersion: user.TermsAcceptedVersion);
    }
}

using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Queries;

public record GetProfileQuery : IRequest<UserProfileDto>;

// GET /me lleva [SkipMembershipValidation] (PLAN-0018 RF-14): no hay IMembershipContext poblado, así que el rol en la
// organización activa se lee de la membresía vigente con IUserMembershipReader, nunca del claim (SR-0018.7). Sin
// membresía vigente en la organización del token responde 404, como switch (RF-16). Con la sesión sin organización
// activa (RF-85) devuelve el perfil con los campos de organización en null.
public sealed class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto>
{
    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly ITenantContext          _tenant;
    private readonly IPlanResolutionService  _plans;
    private readonly IUserMembershipReader   _memberships;

    public GetProfileQueryHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        ITenantContext          tenant,
        IPlanResolutionService  plans,
        IUserMembershipReader   memberships)
    {
        _users         = users;
        _organizations = organizations;
        _tenant        = tenant;
        _plans         = plans;
        _memberships   = memberships;
    }

    public async Task<UserProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(_tenant.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException("User", _tenant.UserId);

        // Sesión sin organización activa (RF-85, D-44): perfil del usuario sin los datos de organización.
        if (!_tenant.HasActiveOrganization)
            return ToDto(user, plan: null, legalType: null, activeOrgId: null, activeOrgName: null, activeOrgRole: null);

        var org = await _organizations.GetByIdAsync(_tenant.OrganizationId, cancellationToken);
        if (org is null)
            throw new NotFoundException("Organization", _tenant.OrganizationId);

        var membership = await _memberships.FindActiveAsync(user.UserId, org.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization", _tenant.OrganizationId);

        var plan = await _plans.ResolveForOwnerAsync(org.OwnerId, cancellationToken);

        return ToDto(user, plan, org.LegalType, _tenant.OrganizationId.ToString(), org.Name, membership.Role);
    }

    private static UserProfileDto ToDto(
        User user, string? plan, string? legalType, string? activeOrgId, string? activeOrgName, string? activeOrgRole)
        => new(
            user.UserId,
            user.FirstName,
            user.LastName,
            user.Email,
            plan,
            legalType,
            user.Role,
            ActiveOrgId:          activeOrgId,
            ActiveOrgName:        activeOrgName,
            ActiveOrgRole:        activeOrgRole,
            TermsAcceptedAt:      user.TermsAcceptedAt,
            TermsAcceptedVersion: user.TermsAcceptedVersion,
            HasPassword:          user.PasswordHash is not null,
            HasGoogle:            user.GoogleId is not null,
            EmailVerified:        user.IsEmailVerified);
}

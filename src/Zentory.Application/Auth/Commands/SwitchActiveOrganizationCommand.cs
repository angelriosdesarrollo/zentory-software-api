using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

public record SwitchActiveOrganizationCommand(Guid OrgId) : IRequest<SwitchOrgResponseDto>;

// La organización destino no es la activa del token, así que su membresía y el listado se leen con
// IUserMembershipReader y no a través del filtro de tenant (PLAN-0018 RF-12). Sin membresía vigente en el destino
// responde 404 (CA-19), sin tocar LastActiveOrganizationId. Con membresía, la elección queda como la última organización
// usada para el próximo login (RF-70, D-27): es el único sitio que la escribe.
// No emite refresh ni inicia sesión (RF-90 c, D-47): el access token nuevo lleva el sid del token que llama
// (ITenantContext.SessionId), también desde la sesión sin organización tras restaurar (RF-85 b). Nunca
// User.CurrentSessionId: si un login nuevo confirmó entre el middleware y este comando, copiarlo entregaría a la sesión
// vieja la identidad de la nueva.
public sealed class SwitchActiveOrganizationCommandHandler
    : IRequestHandler<SwitchActiveOrganizationCommand, SwitchOrgResponseDto>
{
    private readonly IUserMembershipReader   _memberships;
    private readonly IOrganizationRepository _organizations;
    private readonly IUserRepository         _users;
    private readonly IJwtService             _jwt;
    private readonly ITenantContext          _tenant;
    private readonly IUnitOfWork             _uow;

    public SwitchActiveOrganizationCommandHandler(
        IUserMembershipReader   memberships,
        IOrganizationRepository organizations,
        IUserRepository         users,
        IJwtService             jwt,
        ITenantContext          tenant,
        IUnitOfWork             uow)
    {
        _uow           = uow;
        _memberships   = memberships;
        _organizations = organizations;
        _users         = users;
        _jwt           = jwt;
        _tenant        = tenant;
    }

    public async Task<SwitchOrgResponseDto> Handle(
        SwitchActiveOrganizationCommand request, CancellationToken ct)
    {
        // Antes de escribir: un token sin sid no cambia LastActiveOrganizationId.
        var sessionId = _tenant.SessionId;

        var membership = await _memberships.FindActiveAsync(_tenant.UserId, request.OrgId, ct)
            ?? throw new NotFoundException("Organization", request.OrgId);

        var org = await _organizations.GetByIdAsync(request.OrgId, ct)
            ?? throw new NotFoundException("Organization", request.OrgId);

        var user = await _users.GetByIdAsync(_tenant.UserId, ct)
            ?? throw new NotFoundException("User", _tenant.UserId);

        user.SetLastActiveOrganization(org.OrganizationId);
        await _uow.SaveChangesAsync(ct);

        var memberships = await _memberships.ListActiveAsync(_tenant.UserId, ct);

        // El plan del destino sale del lector, que ya lo resolvió por el owner (mismo cálculo que ResolveForOwnerAsync).
        var activePlan  = membership.Plan;
        var accessToken = _jwt.GenerateAccessToken(user, org, membership.Role, activePlan, sessionId);

        return new SwitchOrgResponseDto(
            accessToken,
            (int)_jwt.AccessTokenLifetime.TotalSeconds,
            new UserProfileDto(
                user.UserId, user.FirstName, user.LastName, user.Email,
                activePlan, org.LegalType, user.Role,
                ActiveOrgId:   org.OrganizationId.ToString(),
                ActiveOrgName: org.Name,
                ActiveOrgRole: membership.Role,
                EmailVerified: user.IsEmailVerified),
            memberships.Select(m => m.ToDto()).ToList());
    }
}

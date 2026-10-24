using MediatR;
using Zentory.Application.CollaboratorPortal.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.CollaboratorPortal.Commands;

// Cambiar de organización activa dentro de una sesión ya abierta — no requiere un nuevo
// magic link, solo re-emite el JWT con otro active_collaborator_id/active_org_id.
public sealed record SwitchActiveCollaboratorCommand(Guid CollaboratorId) : IRequest<CollaboratorSessionDto>;

public sealed class SwitchActiveCollaboratorCommandHandler
    : IRequestHandler<SwitchActiveCollaboratorCommand, CollaboratorSessionDto>
{
    private const int SessionDurationSeconds = 7 * 24 * 3600;

    private readonly ICollaboratorPortalContext _portal;
    private readonly ICollaboratorRepository    _collaborators;
    private readonly IOrganizationRepository    _organizations;
    private readonly ICollaboratorJwtService    _jwt;

    public SwitchActiveCollaboratorCommandHandler(
        ICollaboratorPortalContext portal,
        ICollaboratorRepository    collaborators,
        IOrganizationRepository    organizations,
        ICollaboratorJwtService    jwt)
    {
        _portal        = portal;
        _collaborators = collaborators;
        _organizations = organizations;
        _jwt           = jwt;
    }

    public async Task<CollaboratorSessionDto> Handle(
        SwitchActiveCollaboratorCommand request, CancellationToken cancellationToken)
    {
        if (!_portal.CollaboratorIds.Contains(request.CollaboratorId))
            throw new ConflictException("NOT_A_MEMBERSHIP", "Esa organización no pertenece a tu sesión.");

        // Solo una sesión de magic link es multi-org. Una de solicitud, o una emitida antes del
        // claim (AuthMethod null), no puede re-emitirse para otra organización (H-01, D-4).
        if (_portal.AuthMethod != CollaboratorAuthMethod.MagicLink)
            throw new ConflictException("SESSION_NOT_SWITCHABLE", "Para cambiar de organización entra con un enlace de acceso nuevo.");

        var memberships = await _collaborators.ListByEmailAsync(_portal.Email, cancellationToken);
        var active = memberships.FirstOrDefault(m => m.Id == request.CollaboratorId)
            ?? throw new NotFoundException("Collaborator", request.CollaboratorId);

        var orgIds = memberships.Select(m => m.OrganizationId).Distinct().ToList();
        var orgs   = new Dictionary<Guid, string>();
        foreach (var orgId in orgIds)
        {
            var org = await _organizations.GetByIdAsync(orgId, cancellationToken);
            orgs[orgId] = org?.Name ?? string.Empty;
        }

        var membershipDtos = memberships
            .Select(m => new CollaboratorMembershipDto(m.Id, m.OrganizationId, orgs[m.OrganizationId], m.Name))
            .ToList();
        var activeDto = membershipDtos.First(m => m.CollaboratorId == active.Id);

        var accessToken = _jwt.GenerateSessionToken(
            _portal.Email, memberships.Select(m => m.Id).ToList(), active.Id, active.OrganizationId, orgs[active.OrganizationId],
            CollaboratorAuthMethod.MagicLink);

        return new CollaboratorSessionDto(accessToken, SessionDurationSeconds, activeDto, membershipDtos, Highlight: null);
    }
}

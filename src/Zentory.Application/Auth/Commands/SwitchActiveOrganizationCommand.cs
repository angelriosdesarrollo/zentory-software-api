using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

public record SwitchActiveOrganizationCommand(Guid OrgId) : IRequest<SwitchOrgResponseDto>;

public sealed class SwitchActiveOrganizationCommandHandler
    : IRequestHandler<SwitchActiveOrganizationCommand, SwitchOrgResponseDto>
{
    private const int AccessTokenExpiresInSeconds = 15 * 60;

    private readonly IZentoryDbContext       _db;
    private readonly IOrganizationRepository _organizations;
    private readonly IUserRepository         _users;
    private readonly IJwtService             _jwt;
    private readonly ITenantContext          _tenant;
    private readonly IPlanResolutionService  _plans;

    public SwitchActiveOrganizationCommandHandler(
        IZentoryDbContext       db,
        IOrganizationRepository organizations,
        IUserRepository         users,
        IJwtService             jwt,
        ITenantContext          tenant,
        IPlanResolutionService  plans)
    {
        _db            = db;
        _organizations = organizations;
        _users         = users;
        _jwt           = jwt;
        _tenant        = tenant;
        _plans         = plans;
    }

    public async Task<SwitchOrgResponseDto> Handle(
        SwitchActiveOrganizationCommand request, CancellationToken ct)
    {
        var membership = await _db.OrganizationMembers
            .Where(m => m.UserId == _tenant.UserId
                     && m.OrganizationId == request.OrgId
                     && m.DeletedAt == null)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Organization", request.OrgId);

        var org = await _organizations.GetByIdAsync(request.OrgId, ct)
            ?? throw new NotFoundException("Organization", request.OrgId);

        var user = await _users.GetByIdAsync(_tenant.UserId, ct)
            ?? throw new NotFoundException("User", _tenant.UserId);

        var membershipRows = await _db.OrganizationMembers
            .Where(m => m.UserId == _tenant.UserId && m.DeletedAt == null)
            .Join(_db.Organizations,
                m => m.OrganizationId,
                o => o.OrganizationId,
                (m, o) => new { o.OrganizationId, o.Name, o.LegalType, o.OwnerId, m.Role, m.JoinedAt })
            .ToListAsync(ct);

        var plansByOwner = await _plans.ResolveForOwnersAsync(
            membershipRows.Where(r => r.OwnerId.HasValue).Select(r => r.OwnerId!.Value), ct);

        var memberships = membershipRows
            .Select(r => new OrgMembershipDto(
                r.OrganizationId.ToString(),
                r.Name,
                r.LegalType,
                r.OwnerId.HasValue ? plansByOwner.GetValueOrDefault(r.OwnerId.Value, Zentory.Domain.Constants.Plan.Free) : Zentory.Domain.Constants.Plan.Free,
                r.Role,
                r.JoinedAt.ToString("O")))
            .ToList();

        var activePlan  = await _plans.ResolveForOwnerAsync(org.OwnerId, ct);
        var accessToken = _jwt.GenerateAccessToken(user, org, membership.Role, activePlan);

        return new SwitchOrgResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            new UserProfileDto(
                user.UserId, user.FirstName, user.LastName, user.Email,
                activePlan, org.LegalType, user.Role,
                ActiveOrgId:   org.OrganizationId.ToString(),
                ActiveOrgName: org.Name,
                ActiveOrgRole: membership.Role),
            memberships);
    }
}

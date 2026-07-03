using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using DomainValidationException = Zentory.Application.Exceptions.ValidationException;
using DomainValidationError     = Zentory.Application.Exceptions.ValidationError;

namespace Zentory.Application.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken, Guid? ActiveOrgId = null) : IRequest<AuthTokenDto>;

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokenDto>
{
    private const int AccessTokenExpiresInSeconds = 15 * 60; // 15 minutes
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IJwtService             _jwt;
    private readonly IUnitOfWork             _uow;
    private readonly IZentoryDbContext       _db;
    private readonly IPlanResolutionService  _plans;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IUserRepository         users,
        IOrganizationRepository organizations,
        IJwtService             jwt,
        IUnitOfWork             uow,
        IZentoryDbContext       db,
        IPlanResolutionService  plans)
    {
        _refreshTokens = refreshTokens;
        _users         = users;
        _organizations = organizations;
        _jwt           = jwt;
        _uow           = uow;
        _db            = db;
        _plans         = plans;
    }

    public async Task<AuthTokenDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        static DomainValidationException InvalidToken() =>
            new([new DomainValidationError("refreshToken", "El refresh token es inválido o ha expirado.")]);

        var existing = await _refreshTokens.GetByTokenAsync(request.RefreshToken, cancellationToken);

        if (existing is null || existing.IsRevoked || existing.ExpiresAt <= DateTime.UtcNow)
            throw InvalidToken();

        var user = await _users.GetByIdAsync(existing.UserId, cancellationToken);
        if (user is null)
            throw InvalidToken();

        // Resolve active org: honor client's stored preference, fall back to first owner org
        OrganizationMember? activeMembership = null;
        if (request.ActiveOrgId is not null)
        {
            activeMembership = await _db.OrganizationMembers
                .Where(m => m.UserId == user.UserId && m.OrganizationId == request.ActiveOrgId && m.DeletedAt == null)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (activeMembership is null)
        {
            activeMembership = await _db.OrganizationMembers
                .Where(m => m.UserId == user.UserId && m.DeletedAt == null)
                .OrderBy(m => m.Role == "owner" ? 0 : 1)
                .ThenBy(m => m.JoinedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (activeMembership is null)
            throw InvalidToken();

        var org = await _organizations.GetByIdAsync(activeMembership.OrganizationId, cancellationToken);
        if (org is null)
            throw InvalidToken();

        var memberships = await BuildMembershipsAsync(user.UserId, cancellationToken);
        var activePlan  = await _plans.ResolveForOwnerAsync(org.OwnerId, cancellationToken);

        // Revoke old token (rotation)
        existing.Revoke();
        await _refreshTokens.UpdateAsync(existing, cancellationToken);

        // Issue new pair
        var accessToken      = _jwt.GenerateAccessToken(user, org, activeMembership.Role, activePlan);
        var newRefreshToken  = _jwt.GenerateRefreshToken();
        var rt = RefreshToken.Create(user.UserId, newRefreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(
            accessToken,
            newRefreshToken,
            AccessTokenExpiresInSeconds,
            new UserProfileDto(
                user.UserId, user.FirstName, user.LastName, user.Email,
                activePlan, org.LegalType, user.Role,
                ActiveOrgId:   org.OrganizationId.ToString(),
                ActiveOrgName: org.Name,
                ActiveOrgRole: activeMembership.Role),
            memberships);
    }

    private async Task<IReadOnlyList<OrgMembershipDto>> BuildMembershipsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await _db.OrganizationMembers
            .Where(m => m.UserId == userId && m.DeletedAt == null)
            .Join(_db.Organizations,
                m => m.OrganizationId,
                o => o.OrganizationId,
                (m, o) => new { o.OrganizationId, o.Name, o.LegalType, o.OwnerId, m.Role, m.JoinedAt })
            .ToListAsync(ct);

        var plansByOwner = await _plans.ResolveForOwnersAsync(
            rows.Where(r => r.OwnerId.HasValue).Select(r => r.OwnerId!.Value), ct);

        return rows
            .Select(r => new OrgMembershipDto(
                r.OrganizationId.ToString(),
                r.Name,
                r.LegalType,
                r.OwnerId.HasValue ? plansByOwner.GetValueOrDefault(r.OwnerId.Value, Zentory.Domain.Constants.Plan.Free) : Zentory.Domain.Constants.Plan.Free,
                r.Role,
                r.JoinedAt.ToString("O")))
            .ToList();
    }
}

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

public record LoginCommand(string Email, string Password) : IRequest<AuthTokenDto>;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthTokenDto>
{
    private const int AccessTokenExpiresInSeconds = 15 * 60; // 15 minutes
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtService             _jwt;
    private readonly IUnitOfWork             _uow;
    private readonly IZentoryDbContext       _db;
    private readonly IPlanResolutionService  _plans;

    public LoginCommandHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        IRefreshTokenRepository refreshTokens,
        IJwtService             jwt,
        IUnitOfWork             uow,
        IZentoryDbContext       db,
        IPlanResolutionService  plans)
    {
        _users         = users;
        _organizations = organizations;
        _refreshTokens = refreshTokens;
        _jwt           = jwt;
        _uow           = uow;
        _db            = db;
        _plans         = plans;
    }

    public async Task<AuthTokenDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Generic error message — do not reveal whether email or password is wrong
        static DomainValidationException InvalidCredentials() =>
            new([new DomainValidationError("credentials", "Credenciales inválidas.")]);

        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null)
            throw InvalidCredentials();

        if (user.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw InvalidCredentials();

        // Resolve active org via OrganizationMember (first owned org, then any membership)
        var activeMembership = await _db.OrganizationMembers
            .Where(m => m.UserId == user.UserId && m.DeletedAt == null)
            .OrderBy(m => m.Role == "owner" ? 0 : 1)
            .ThenBy(m => m.JoinedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeMembership is null)
            throw InvalidCredentials();

        var org = await _organizations.GetByIdAsync(activeMembership.OrganizationId, cancellationToken)
            ?? throw InvalidCredentials();

        var memberships = await BuildMembershipsAsync(user.UserId, cancellationToken);
        var activePlan  = await _plans.ResolveForOwnerAsync(org.OwnerId, cancellationToken);

        var accessToken  = _jwt.GenerateAccessToken(user, org, activeMembership.Role, activePlan);
        var refreshToken = _jwt.GenerateRefreshToken();

        var rt = RefreshToken.Create(user.UserId, refreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(
            accessToken,
            refreshToken,
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

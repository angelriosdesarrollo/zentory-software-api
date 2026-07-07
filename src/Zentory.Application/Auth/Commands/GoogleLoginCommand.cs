using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Repositories;
using LegalTypeConstants = Zentory.Domain.Constants.LegalType;
using PlanConstants      = Zentory.Domain.Constants.Plan;

namespace Zentory.Application.Auth.Commands;

public record GoogleLoginCommand(string IdToken) : IRequest<AuthTokenDto>;

public sealed class GoogleLoginCommandHandler : IRequestHandler<GoogleLoginCommand, AuthTokenDto>
{
    private const int AccessTokenExpiresInSeconds = 15 * 60; // 15 minutes
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtService             _jwt;
    private readonly IGoogleTokenValidator   _google;
    private readonly IUnitOfWork             _uow;
    private readonly IZentoryDbContext       _db;
    private readonly IPlanResolutionService  _plans;

    public GoogleLoginCommandHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        IRefreshTokenRepository refreshTokens,
        IJwtService             jwt,
        IGoogleTokenValidator   google,
        IUnitOfWork             uow,
        IZentoryDbContext       db,
        IPlanResolutionService  plans)
    {
        _users         = users;
        _organizations = organizations;
        _refreshTokens = refreshTokens;
        _jwt           = jwt;
        _google        = google;
        _uow           = uow;
        _db            = db;
        _plans         = plans;
    }

    public async Task<AuthTokenDto> Handle(GoogleLoginCommand request, CancellationToken cancellationToken)
    {
        var googleUser = await _google.ValidateAsync(request.IdToken, cancellationToken);

        var user = await _users.GetByGoogleIdAsync(googleUser.Subject, cancellationToken);

        if (user is null)
        {
            var existingByEmail = await _users.GetByEmailAsync(googleUser.Email, cancellationToken);
            if (existingByEmail is not null)
            {
                existingByEmail.LinkGoogleAccount(googleUser.Subject);
                user = existingByEmail;
            }
        }

        if (user is not null)
            return await BuildExistingUserResponseAsync(user, cancellationToken);

        return await CreateUserAndOrgAsync(googleUser, cancellationToken);
    }

    private async Task<AuthTokenDto> BuildExistingUserResponseAsync(User user, CancellationToken ct)
    {
        var activeMembership = await _db.OrganizationMembers
            .Where(m => m.UserId == user.UserId && m.DeletedAt == null)
            .OrderBy(m => m.Role == "owner" ? 0 : 1)
            .ThenBy(m => m.JoinedAt)
            .FirstOrDefaultAsync(ct);

        if (activeMembership is null)
            throw new InvalidOperationException($"User {user.UserId} has no organization membership.");

        var org = await _organizations.GetByIdAsync(activeMembership.OrganizationId, ct)
            ?? throw new InvalidOperationException($"Organization {activeMembership.OrganizationId} referenced by membership not found.");

        var memberships = await BuildMembershipsAsync(user.UserId, ct);
        var activePlan  = await _plans.ResolveForOwnerAsync(org.OwnerId, ct);

        var accessToken  = _jwt.GenerateAccessToken(user, org, activeMembership.Role, activePlan);
        var refreshToken = _jwt.GenerateRefreshToken();

        var rt = RefreshToken.Create(user.UserId, refreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, ct);
        await _uow.SaveChangesAsync(ct);

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
            memberships,
            IsNewUser: false);
    }

    // Reached for a first-time Google sign-in with no prior account. Mirrors
    // RegisterCommandHandler's "new owner" path: placeholder org named after the
    // user's first name, Freelance/CO defaults — refined later in onboarding.
    private async Task<AuthTokenDto> CreateUserAndOrgAsync(GoogleUserInfo googleUser, CancellationToken ct)
    {
        var org = global::Zentory.Domain.Entities.Organization.Create(googleUser.FirstName, LegalTypeConstants.Freelance, "CO");
        await _organizations.AddAsync(org, ct);

        var user = User.Create(
            googleUser.Email,
            googleUser.FirstName,
            googleUser.LastName,
            role: "owner",
            googleId: googleUser.Subject);
        await _users.AddAsync(user, ct);

        org.SetOwner(user.UserId);
        _db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, user.UserId, "owner"));

        var freePlan = await _db.BillingPlans.FirstOrDefaultAsync(p => p.Name == PlanConstants.Free, ct)
            ?? throw new InvalidOperationException("Free billing plan is not seeded.");

        var customer = BillingCustomer.Create(
            org.OrganizationId, gatewayCustomerId: $"local_{user.UserId}", user.Email, org.Name);
        _db.BillingCustomers.Add(customer);

        var subscription = Subscription.Create(user.UserId, customer.Id, freePlan.Id);
        _db.Subscriptions.Add(subscription);

        var accessToken  = _jwt.GenerateAccessToken(user, org, activeOrgRole: "owner", plan: PlanConstants.Free);
        var refreshToken = _jwt.GenerateRefreshToken();

        var rt = RefreshToken.Create(user.UserId, refreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, ct);

        await _uow.SaveChangesAsync(ct);

        var memberships = new List<OrgMembershipDto>
        {
            new(org.OrganizationId.ToString(), org.Name, org.LegalType, PlanConstants.Free, "owner",
                DateTime.UtcNow.ToString("O"))
        };

        return new AuthTokenDto(
            accessToken,
            refreshToken,
            AccessTokenExpiresInSeconds,
            new UserProfileDto(
                user.UserId, user.FirstName, user.LastName, user.Email,
                PlanConstants.Free, org.LegalType, user.Role,
                ActiveOrgId:   org.OrganizationId.ToString(),
                ActiveOrgName: org.Name,
                ActiveOrgRole: "owner"),
            memberships,
            IsNewUser: true);
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
                r.OwnerId.HasValue ? plansByOwner.GetValueOrDefault(r.OwnerId.Value, PlanConstants.Free) : PlanConstants.Free,
                r.Role,
                r.JoinedAt.ToString("O")))
            .ToList();
    }
}

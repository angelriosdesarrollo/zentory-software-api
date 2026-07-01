using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Repositories;
using PlanConstants = Zentory.Domain.Constants.Plan;

namespace Zentory.Application.Auth.Commands;

public record RegisterCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string OrgName,
    string AccountType,
    string Country = "CO") : IRequest<AuthTokenDto>;

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthTokenDto>
{
    private const int AccessTokenExpiresInSeconds = 15 * 60; // 15 minutes
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtService             _jwt;
    private readonly IUnitOfWork             _uow;
    private readonly IZentoryDbContext       _db;

    public RegisterCommandHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        IRefreshTokenRepository refreshTokens,
        IJwtService             jwt,
        IUnitOfWork             uow,
        IZentoryDbContext       db)
    {
        _users         = users;
        _organizations = organizations;
        _refreshTokens = refreshTokens;
        _jwt           = jwt;
        _uow           = uow;
        _db            = db;
    }

    public async Task<AuthTokenDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var existing = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
            throw new ConflictException("EMAIL_ALREADY_EXISTS", "El correo electrónico ya está registrado.");

        var org = global::Zentory.Domain.Entities.Organization.Create(request.OrgName, request.AccountType, request.Country);
        await _organizations.AddAsync(org, cancellationToken);

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var user = User.Create(
            request.Email,
            request.FirstName,
            request.LastName,
            role: "owner",
            passwordHash: passwordHash);

        await _users.AddAsync(user, cancellationToken);

        org.SetOwner(user.UserId);
        _db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, user.UserId, "owner"));

        // New owners start on the Free plan. The subscription belongs to the user (not the
        // org) and will apply to every additional org this user goes on to own.
        var freePlan = await _db.BillingPlans.FirstOrDefaultAsync(p => p.Name == PlanConstants.Free, cancellationToken)
            ?? throw new InvalidOperationException("Free billing plan is not seeded.");

        var customer = BillingCustomer.Create(
            org.OrganizationId, gatewayCustomerId: $"local_{user.UserId}", user.Email, org.Name);
        _db.BillingCustomers.Add(customer);

        var subscription = Subscription.Create(user.UserId, customer.Id, freePlan.Id);
        _db.Subscriptions.Add(subscription);

        var accessToken  = _jwt.GenerateAccessToken(user, org, activeOrgRole: "owner", plan: PlanConstants.Free);
        var refreshToken = _jwt.GenerateRefreshToken();

        var rt = RefreshToken.Create(user.UserId, refreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        var memberships = new List<OrgMembershipDto>
        {
            new(org.OrganizationId.ToString(), org.Name, org.AccountType, PlanConstants.Free, "owner",
                DateTime.UtcNow.ToString("O"))
        };

        return new AuthTokenDto(
            accessToken,
            refreshToken,
            AccessTokenExpiresInSeconds,
            new UserProfileDto(
                user.UserId, user.FirstName, user.LastName, user.Email,
                PlanConstants.Free, org.AccountType, user.Role,
                ActiveOrgId:   org.OrganizationId.ToString(),
                ActiveOrgName: org.Name,
                ActiveOrgRole: "owner"),
            memberships);
    }
}

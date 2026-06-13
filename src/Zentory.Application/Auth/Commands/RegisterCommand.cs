using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

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

    public RegisterCommandHandler(
        IUserRepository         users,
        IOrganizationRepository organizations,
        IRefreshTokenRepository refreshTokens,
        IJwtService             jwt,
        IUnitOfWork             uow)
    {
        _users         = users;
        _organizations = organizations;
        _refreshTokens = refreshTokens;
        _jwt           = jwt;
        _uow           = uow;
    }

    public async Task<AuthTokenDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var existing = await _users.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
            throw new ConflictException("EMAIL_ALREADY_EXISTS", "El correo electrónico ya está registrado.");

        var org = Organization.Create(request.OrgName, request.AccountType, request.Country);
        await _organizations.AddAsync(org, cancellationToken);

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var user = User.Create(
            org.OrganizationId,
            request.Email,
            request.FirstName,
            request.LastName,
            role: "owner",
            passwordHash: passwordHash);

        await _users.AddAsync(user, cancellationToken);

        var accessToken  = _jwt.GenerateAccessToken(user, org);
        var refreshToken = _jwt.GenerateRefreshToken();

        var rt = RefreshToken.Create(user.UserId, refreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(
            accessToken,
            refreshToken,
            AccessTokenExpiresInSeconds,
            new UserProfileDto(user.UserId, user.FirstName, user.LastName, user.Email, org.Plan, org.AccountType, user.Role, org.Name));
    }
}

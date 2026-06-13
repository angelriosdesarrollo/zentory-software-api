using MediatR;
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

    public LoginCommandHandler(
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

        var org = await _organizations.GetByIdAsync(user.OrganizationId, cancellationToken)
            ?? throw InvalidCredentials();

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

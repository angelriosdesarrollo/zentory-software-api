using MediatR;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using DomainValidationException = Zentory.Application.Exceptions.ValidationException;
using DomainValidationError     = Zentory.Application.Exceptions.ValidationError;

namespace Zentory.Application.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthTokenDto>;

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokenDto>
{
    private const int AccessTokenExpiresInSeconds = 15 * 60; // 15 minutes
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserRepository         _users;
    private readonly IOrganizationRepository _organizations;
    private readonly IJwtService             _jwt;
    private readonly IUnitOfWork             _uow;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokens,
        IUserRepository         users,
        IOrganizationRepository organizations,
        IJwtService             jwt,
        IUnitOfWork             uow)
    {
        _refreshTokens = refreshTokens;
        _users         = users;
        _organizations = organizations;
        _jwt           = jwt;
        _uow           = uow;
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

        var org = await _organizations.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (org is null)
            throw InvalidToken();

        // Revoke old token (rotation)
        existing.Revoke();
        await _refreshTokens.UpdateAsync(existing, cancellationToken);

        // Issue new pair
        var accessToken      = _jwt.GenerateAccessToken(user, org);
        var newRefreshToken  = _jwt.GenerateRefreshToken();
        var rt = RefreshToken.Create(user.UserId, newRefreshToken, DateTime.UtcNow.Add(RefreshTokenLifetime));
        await _refreshTokens.AddAsync(rt, cancellationToken);

        await _uow.SaveChangesAsync(cancellationToken);

        return new AuthTokenDto(
            accessToken,
            newRefreshToken,
            AccessTokenExpiresInSeconds,
            new UserProfileDto(user.UserId, user.FirstName, user.LastName, user.Email, org.Plan, org.AccountType, user.Role, org.Name));
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken, Guid? ActiveOrgId = null) : IRequest<AuthTokenDto>;

// 401 { error: "unauthorized", code: "REFRESH_TOKEN_INVALID" } (PLAN-0018 RF-30 c, D-9): el mismo cuerpo para un token
// inexistente, expirado, revocado o reutilizado y para un usuario sin membresías ni organizaciones restaurables (RF-61),
// para no revelar cuál fue. El frontend lo trata como sesión terminada.
public static class RefreshTokenErrors
{
    public const string InvalidCode    = "REFRESH_TOKEN_INVALID";
    public const string InvalidMessage = "El refresh token es inválido o ha expirado.";

    public static UnauthorizedException Invalid() => new(InvalidCode, InvalidMessage);
}

// Rotación del refresh token (PLAN-0018 RF-30, RF-90 e; SR-0018.13, D-12, D-47). El orden importa:
// 1. El token se busca por su hash. Inexistente, expirado o sin sesión (anterior a M2c): 401 REFRESH_TOKEN_INVALID.
// 2. Si su sesión no es la vigente del usuario (otro login la reemplazó), esté revocado o no: 401 SESSION_REPLACED, sin
//    escribir nada. Así el refresh de la sesión vieja nunca dispara el reuso, que revocaría también la sesión nueva.
// 3. De la sesión vigente y ya revocado (rotado antes): reuso, se revocan todos los del usuario y 401 (RF-30 b).
// 4. El token se reclama con un SaveChanges propio: IsRevoked es token de concurrencia, así que de dos refresh simultáneos
//    con el mismo token solo uno actualiza la fila. Si el reclamo choca, se vuelve a leer la sesión vigente sin
//    seguimiento: si ya no es la del token, el conflicto lo causó un login que revocó los tokens anteriores, y es
//    SESSION_REPLACED sin revocar nada; si sigue siendo la misma, es una rotación simultánea y se aplica el reuso de 3.
//    Se reclama antes de emitir para que el perdedor nunca deje un token nuevo insertado.
// 5. Se emite el par nuevo en la misma sesión (ContinueSessionAsync). Respeta la organización que pide el cliente solo si
//    la membresía sigue vigente (RF-16); si no, la regla general de IAuthSessionIssuer, sin LastActiveOrganizationId. Sin
//    membresías vigentes: sesión sin organización si tiene organizaciones restaurables (RF-85); si no, el mismo 401 de un
//    token inválido (RF-61), nunca 500. En ese caso el token presentado ya quedó consumido.
public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokenDto>
{
    private const int MaxRevokeAllAttempts = 3;

    private readonly IRefreshTokenRepository             _refreshTokens;
    private readonly IUserRepository                     _users;
    private readonly IZentoryDbContext                   _db;
    private readonly IAuthSessionIssuer                  _sessions;
    private readonly IUnitOfWork                         _uow;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository             refreshTokens,
        IUserRepository                     users,
        IZentoryDbContext                   db,
        IAuthSessionIssuer                  sessions,
        IUnitOfWork                         uow,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _users         = users;
        _db            = db;
        _sessions      = sessions;
        _uow           = uow;
        _logger        = logger;
    }

    public async Task<AuthTokenDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var existing = string.IsNullOrEmpty(request.RefreshToken)
            ? null
            : await _refreshTokens.GetByHashAsync(RefreshToken.HashOf(request.RefreshToken), cancellationToken);

        if (existing is null || existing.IsExpired(DateTime.UtcNow) || existing.SessionId is not { } sessionId)
            throw RefreshTokenErrors.Invalid();

        var user = await _users.GetByIdAsync(existing.UserId, cancellationToken);
        if (user is null)
            throw RefreshTokenErrors.Invalid();

        if (user.CurrentSessionId != sessionId)
            throw new SessionReplacedException();

        if (existing.IsRevoked)
        {
            await RevokeAllOnReuseAsync(existing.UserId, cancellationToken);
            throw RefreshTokenErrors.Invalid();
        }

        existing.Revoke();
        await _refreshTokens.UpdateAsync(existing, cancellationToken);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Otra operación revocó el token entre la lectura y este guardado: un login nuevo o un refresh simultáneo.
            exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            if (await ReadCurrentSessionIdAsync(existing.UserId, cancellationToken) != sessionId)
                throw new SessionReplacedException();

            await RevokeAllOnReuseAsync(existing.UserId, cancellationToken);
            throw RefreshTokenErrors.Invalid();
        }

        try
        {
            return await _sessions.ContinueSessionAsync(user, sessionId, request.ActiveOrgId, cancellationToken);
        }
        catch (NoActiveMembershipException)
        {
            throw RefreshTokenErrors.Invalid();
        }
    }

    // Lectura sin seguimiento: el usuario cargado antes del reclamo tiene el valor viejo.
    private Task<Guid?> ReadCurrentSessionIdAsync(Guid userId, CancellationToken ct)
        => _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.CurrentSessionId)
            .FirstOrDefaultAsync(ct);

    // Reuso (RF-30 b): se revocan todos los refresh tokens del usuario, también el par vigente. Si otra operación revocó
    // alguno entre medias, se descarta lo pendiente y se vuelve a leer.
    private async Task RevokeAllOnReuseAsync(Guid userId, CancellationToken ct)
    {
        _logger.LogWarning("Reuso de refresh token: se revocan todos los refresh tokens del usuario {UserId}.", userId);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _refreshTokens.RevokeAllForUserAsync(userId, ct);
                await _uow.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < MaxRevokeAllAttempts)
            {
                exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            }
        }
    }
}

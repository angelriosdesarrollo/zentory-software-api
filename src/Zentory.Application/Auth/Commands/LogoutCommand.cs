using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Application.Auth.Commands;

public record LogoutCommand(string? RefreshToken) : IRequest;

// POST /auth/logout (PLAN-0018 RF-30 d, RF-90 f; D-47, C-17): revoca solo el refresh token presentado. Siempre termina
// sin error (204): un token inexistente, ya revocado o sin sesión no cambia nada y no revela si existía. Con el refresh de
// una sesión reemplazada tampoco escribe: esa sesión ya terminó y la vigente no se toca. No cambia CurrentSessionId, así
// que el access token de la sesión sigue valiendo hasta expirar. Si un refresh simultáneo rotó el token entre la lectura
// y el guardado, se revocan los de esa misma sesión, para que el par nuevo tampoco sobreviva al logout, sin tocar otra
// sesión ni otro usuario.
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IZentoryDbContext       _db;
    private readonly IUnitOfWork             _uow;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokens, IZentoryDbContext db, IUnitOfWork uow)
    {
        _refreshTokens = refreshTokens;
        _db            = db;
        _uow           = uow;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
            return;

        var existing = await _refreshTokens.GetByHashAsync(RefreshToken.HashOf(request.RefreshToken), cancellationToken);
        if (existing is null || existing.IsRevoked || existing.SessionId is not { } sessionId)
            return;

        var currentSessionId = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == existing.UserId)
            .Select(u => u.CurrentSessionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (currentSessionId != sessionId)
            return;

        existing.Revoke();
        await _refreshTokens.UpdateAsync(existing, cancellationToken);
        try
        {
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            exception.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();
            await _refreshTokens.RevokeAllForSessionAsync(existing.UserId, sessionId, cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ZentoryDbContext _db;

    public RefreshTokenRepository(ZentoryDbContext db) => _db = db;

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default)
        => await _db.RefreshTokens.AddAsync(refreshToken, ct);

    public Task UpdateAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        _db.RefreshTokens.Update(refreshToken);
        return Task.CompletedTask;
    }

    // Con entidades rastreadas y no con ExecuteUpdate: el proveedor InMemory de la suite no lo soporta, y así la
    // revocación entra en el mismo SaveChanges que el resto de la operación del llamador.
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens.Where(rt => rt.UserId == userId && !rt.IsRevoked).ToListAsync(ct);
        foreach (var token in active)
            token.Revoke();
    }

    public async Task RevokeAllForSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.SessionId == sessionId && !rt.IsRevoked)
            .ToListAsync(ct);
        foreach (var token in active)
            token.Revoke();
    }
}

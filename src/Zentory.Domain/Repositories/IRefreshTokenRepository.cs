using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IRefreshTokenRepository
{
    // Busca por el hash SHA-256 (RefreshToken.HashOf); la base nunca tiene el token en claro (PLAN-0018 RF-30).
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default);
    Task UpdateAsync(RefreshToken refreshToken, CancellationToken ct = default);

    // Marca como revocados todos los refresh tokens vigentes del usuario; los guarda el SaveChanges del llamador.
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);

    // Igual, pero solo los de una sesión del usuario (PLAN-0018 RF-90 f, D-47, C-17): el respaldo del logout ante una
    // rotación simultánea no toca otra sesión.
    Task RevokeAllForSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
}

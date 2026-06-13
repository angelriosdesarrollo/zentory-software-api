using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ZentoryDbContext _db;

    public RefreshTokenRepository(ZentoryDbContext db) => _db = db;

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == token, ct);

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default)
        => await _db.RefreshTokens.AddAsync(refreshToken, ct);

    public Task UpdateAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        _db.RefreshTokens.Update(refreshToken);
        return Task.CompletedTask;
    }
}

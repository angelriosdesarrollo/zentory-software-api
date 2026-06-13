using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ZentoryDbContext _db;

    public UserRepository(ZentoryDbContext db) => _db = db;

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken ct = default)
        => await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await _db.Users.AddAsync(user, ct);
}

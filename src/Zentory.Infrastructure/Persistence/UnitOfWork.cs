using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ZentoryDbContext _db;
    public UnitOfWork(ZentoryDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}

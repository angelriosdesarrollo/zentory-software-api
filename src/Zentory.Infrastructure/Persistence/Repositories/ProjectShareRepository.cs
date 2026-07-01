using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class ProjectShareRepository : IProjectShareRepository
{
    private readonly ZentoryDbContext _db;

    public ProjectShareRepository(ZentoryDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProjectShare>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _db.ProjectShares
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<ProjectShare?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ProjectShares.FirstOrDefaultAsync(s => s.Id == id, ct);

    // Token lookup bypasses query filters so unauthenticated requests can read the share.
    public async Task<ProjectShare?> GetByTokenAsync(string token, CancellationToken ct = default)
        => await _db.ProjectShares
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Token == token && s.DeletedAt == null, ct);

    public async Task AddAsync(ProjectShare share, CancellationToken ct = default)
        => await _db.ProjectShares.AddAsync(share, ct);

    public Task UpdateAsync(ProjectShare share, CancellationToken ct = default)
    {
        _db.ProjectShares.Update(share);
        return Task.CompletedTask;
    }
}

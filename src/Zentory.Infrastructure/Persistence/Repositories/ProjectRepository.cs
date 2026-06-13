using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly ZentoryDbContext _db;

    public ProjectRepository(ZentoryDbContext db) => _db = db;

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Project>> ListAsync(
        Guid    organizationId,
        string? search   = null,
        string? status   = null,
        Guid?   clientId = null,
        CancellationToken ct = default)
    {
        var query = _db.Projects
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ProjectStatus>(status, ignoreCase: true, out var ps))
            query = query.Where(p => p.Status == ps);

        if (clientId.HasValue)
            query = query.Where(p => p.ClientId == clientId.Value);

        return await query.OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Project>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Projects
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Project>> ListByClientAsync(Guid clientId, CancellationToken ct = default)
        => await _db.Projects
            .Where(p => p.ClientId == clientId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<int> CountAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Projects.CountAsync(p => p.OrganizationId == organizationId && !p.IsDeleted, ct);

    public async Task AddAsync(Project project, CancellationToken ct = default)
        => await _db.Projects.AddAsync(project, ct);

    public Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Update(project);
        return Task.CompletedTask;
    }
}

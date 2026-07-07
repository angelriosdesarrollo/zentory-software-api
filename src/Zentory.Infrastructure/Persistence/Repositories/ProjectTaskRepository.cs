using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class ProjectTaskRepository : IProjectTaskRepository
{
    private readonly ZentoryDbContext _db;

    public ProjectTaskRepository(ZentoryDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => await _db.ProjectTasks
            .Where(t => t.ProjectId == projectId && t.DeletedAt == null)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, (int Total, int Done)>> GetTaskCountsByProjectIdsAsync(
        IEnumerable<Guid> projectIds, Guid organizationId, CancellationToken ct = default)
    {
        var ids = projectIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, (int, int)>();

        var rows = await _db.ProjectTasks
            .Where(t => t.OrganizationId == organizationId && t.DeletedAt == null && ids.Contains(t.ProjectId))
            .GroupBy(t => t.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Total     = g.Count(),
                Done      = g.Count(t => t.Status == "done")
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ProjectId, r => (r.Total, r.Done));
    }

    public async Task<ProjectTask?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct);

    public async Task AddAsync(ProjectTask task, CancellationToken ct = default)
        => await _db.ProjectTasks.AddAsync(task, ct);

    public Task UpdateAsync(ProjectTask task, CancellationToken ct = default)
    {
        _db.ProjectTasks.Update(task);
        return Task.CompletedTask;
    }
}

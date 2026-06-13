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

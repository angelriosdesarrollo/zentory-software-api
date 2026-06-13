using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IProjectTaskRepository
{
    Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ProjectTask task, CancellationToken ct = default);
    Task UpdateAsync(ProjectTask task, CancellationToken ct = default);
}

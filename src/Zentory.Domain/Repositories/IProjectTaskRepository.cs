using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IProjectTaskRepository
{
    Task<IReadOnlyList<ProjectTask>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    /// <summary>Conteo de tareas totales vs. en estado "done" por proyecto, para el factor de tiempo del Health Score.</summary>
    Task<Dictionary<Guid, (int Total, int Done)>> GetTaskCountsByProjectIdsAsync(
        IEnumerable<Guid> projectIds, Guid organizationId, CancellationToken ct = default);
    Task<ProjectTask?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ProjectTask task, CancellationToken ct = default);
    Task UpdateAsync(ProjectTask task, CancellationToken ct = default);
}

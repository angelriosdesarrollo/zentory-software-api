using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IProjectShareRepository
{
    Task<IReadOnlyList<ProjectShare>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<ProjectShare?>               GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProjectShare?>               GetByTokenAsync(string token, CancellationToken ct = default);
    Task                              AddAsync(ProjectShare share, CancellationToken ct = default);
    Task                              UpdateAsync(ProjectShare share, CancellationToken ct = default);
}

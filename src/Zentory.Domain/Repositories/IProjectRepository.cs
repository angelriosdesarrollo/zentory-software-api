using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> ListAsync(Guid organizationId, string? search = null, string? status = null, Guid? clientId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> ListByClientAsync(Guid clientId, CancellationToken ct = default);
    Task<int> CountAsync(Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Project project, CancellationToken ct = default);
    Task UpdateAsync(Project project, CancellationToken ct = default);
}

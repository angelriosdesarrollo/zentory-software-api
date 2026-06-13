using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface ICollaboratorRepository
{
    Task<Collaborator?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Collaborator>> ListAsync(Guid organizationId, string? search = null, string? status = null, CancellationToken ct = default);
    Task<int> CountActiveAsync(Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Collaborator collaborator, CancellationToken ct = default);
    Task UpdateAsync(Collaborator collaborator, CancellationToken ct = default);
}

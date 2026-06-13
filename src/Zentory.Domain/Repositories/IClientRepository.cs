using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IClientRepository
{
    Task<Client?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Client>> ListAsync(Guid organizationId, string? search = null, CancellationToken ct = default);
    Task<int> CountAsync(Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Client client, CancellationToken ct = default);
    Task UpdateAsync(Client client, CancellationToken ct = default);
    Task<bool> HasActiveProjectsOrPendingInvoicesAsync(Guid clientId, CancellationToken ct = default);
}

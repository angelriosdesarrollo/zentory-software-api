using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IProposalRepository
{
    Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Proposal?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Proposal>> ListAsync(Guid organizationId, string? status = null, Guid? clientId = null, string? search = null, CancellationToken ct = default);
    Task AddAsync(Proposal proposal, CancellationToken ct = default);
    Task UpdateAsync(Proposal proposal, CancellationToken ct = default);
}

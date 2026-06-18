using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IProposalRepository
{
    Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Proposal?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Proposal>> ListAsync(Guid organizationId, string? status = null, Guid? clientId = null, string? search = null, CancellationToken ct = default);
    Task AddAsync(Proposal proposal, CancellationToken ct = default);
    Task UpdateAsync(Proposal proposal, CancellationToken ct = default);

    Task<Proposal?> GetByPublicTokenAsync(Guid publicToken, CancellationToken ct = default);

    // Replace-all (DELETE existing + stage INSERT new, committed via IUnitOfWork)
    Task ReplaceSectionsAsync(Guid proposalId, IReadOnlyList<ProposalSection> sections, CancellationToken ct = default);
    Task ReplaceItemsAsync(Guid proposalId, IReadOnlyList<ProposalItem> items, CancellationToken ct = default);
}

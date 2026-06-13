using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface ICashFlowEntryRepository
{
    Task<CashFlowEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CashFlowEntry>> ListAsync(
        Guid      organizationId,
        string?   type      = null,
        DateOnly? from      = null,
        DateOnly? to        = null,
        Guid?     projectId = null,
        CancellationToken ct = default);
    Task AddAsync(CashFlowEntry entry, CancellationToken ct = default);
    Task UpdateAsync(CashFlowEntry entry, CancellationToken ct = default);
}

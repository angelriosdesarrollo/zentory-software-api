using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface ITimeEntryRepository
{
    Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TimeEntry>> ListAsync(
        Guid      organizationId,
        DateTime? from           = null,
        DateTime? to             = null,
        string?   status         = null,
        Guid?     projectId      = null,
        Guid?     collaboratorId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<TimeEntry>> GetByIdsAsync(IEnumerable<Guid> ids, Guid organizationId, CancellationToken ct = default);
    Task AddAsync(TimeEntry entry, CancellationToken ct = default);
    Task UpdateAsync(TimeEntry entry, CancellationToken ct = default);
    Task BatchMarkBilledAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}

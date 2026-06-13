using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class TimeEntryRepository : ITimeEntryRepository
{
    private readonly ZentoryDbContext _db;

    public TimeEntryRepository(ZentoryDbContext db) => _db = db;

    public async Task<TimeEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.TimeEntries.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<TimeEntry>> ListAsync(
        Guid      organizationId,
        DateTime? from           = null,
        DateTime? to             = null,
        string?   status         = null,
        Guid?     projectId      = null,
        Guid?     collaboratorId = null,
        CancellationToken ct = default)
    {
        var query = _db.TimeEntries
            .Where(t => t.OrganizationId == organizationId);

        if (from.HasValue)
            query = query.Where(t => t.Date >= DateOnly.FromDateTime(from.Value));

        if (to.HasValue)
            query = query.Where(t => t.Date <= DateOnly.FromDateTime(to.Value));

        if (status != null)
            query = query.Where(t => t.Status == status);

        if (projectId.HasValue)
            query = query.Where(t => t.ProjectId == projectId.Value);

        if (collaboratorId.HasValue)
            query = query.Where(t => t.CollaboratorId == collaboratorId.Value);

        return await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TimeEntry>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        Guid              organizationId,
        CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return await _db.TimeEntries
            .Where(t => t.OrganizationId == organizationId && idList.Contains(t.Id))
            .ToListAsync(ct);
    }

    public async Task AddAsync(TimeEntry entry, CancellationToken ct = default)
        => await _db.TimeEntries.AddAsync(entry, ct);

    public Task UpdateAsync(TimeEntry entry, CancellationToken ct = default)
    {
        _db.TimeEntries.Update(entry);
        return Task.CompletedTask;
    }

    public async Task BatchMarkBilledAsync(
        IEnumerable<Guid> ids,
        CancellationToken ct = default)
    {
        var idList = ids.ToList();
        await _db.TimeEntries
            .Where(t => idList.Contains(t.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status,    "billed")
                .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);
    }
}

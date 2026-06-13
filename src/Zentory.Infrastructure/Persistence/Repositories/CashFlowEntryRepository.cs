using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class CashFlowEntryRepository : ICashFlowEntryRepository
{
    private readonly ZentoryDbContext _db;

    public CashFlowEntryRepository(ZentoryDbContext db) => _db = db;

    public async Task<CashFlowEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.CashFlowEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<CashFlowEntry>> ListAsync(
        Guid      organizationId,
        string?   type      = null,
        DateOnly? from      = null,
        DateOnly? to        = null,
        Guid?     projectId = null,
        CancellationToken ct = default)
    {
        var query = _db.CashFlowEntries
            .Where(e => e.OrganizationId == organizationId);

        if (type != null)
            query = query.Where(e => e.Type == type);

        if (from.HasValue)
            query = query.Where(e => e.TransactionDate >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.TransactionDate <= to.Value);

        if (projectId.HasValue)
            query = query.Where(e => e.ProjectId == projectId.Value);

        return await query
            .OrderByDescending(e => e.TransactionDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(CashFlowEntry entry, CancellationToken ct = default)
        => await _db.CashFlowEntries.AddAsync(entry, ct);

    public Task UpdateAsync(CashFlowEntry entry, CancellationToken ct = default)
    {
        _db.CashFlowEntries.Update(entry);
        return Task.CompletedTask;
    }
}

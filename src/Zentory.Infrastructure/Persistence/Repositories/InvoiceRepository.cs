using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly ZentoryDbContext _db;

    public InvoiceRepository(ZentoryDbContext db) => _db = db;

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<Invoice?> GetByIdWithItemsAsync(Guid id, CancellationToken ct = default)
        => await _db.Invoices
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<Invoice>> ListAsync(
        Guid    organizationId,
        string? status   = null,
        Guid?   clientId = null,
        CancellationToken ct = default)
    {
        var query = _db.Invoices
            .Where(i => i.OrganizationId == organizationId);

        if (status != null)
            query = query.Where(i => i.Status == status);

        if (clientId.HasValue)
            query = query.Where(i => i.ClientId == clientId.Value);

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<int> CountThisMonthAsync(Guid organizationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Invoices
            .CountAsync(i =>
                i.OrganizationId == organizationId &&
                i.CreatedAt.Year == now.Year &&
                i.CreatedAt.Month == now.Month, ct);
    }

    public async Task AddAsync(Invoice invoice, CancellationToken ct = default)
        => await _db.Invoices.AddAsync(invoice, ct);

    public Task UpdateAsync(Invoice invoice, CancellationToken ct = default)
    {
        _db.Invoices.Update(invoice);
        return Task.CompletedTask;
    }
}

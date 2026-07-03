using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class CollaboratorPayoutInvoiceRepository : ICollaboratorPayoutInvoiceRepository
{
    private readonly ZentoryDbContext _db;

    public CollaboratorPayoutInvoiceRepository(ZentoryDbContext db) => _db = db;

    public async Task<CollaboratorPayoutInvoice?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.CollaboratorPayoutInvoices.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<CollaboratorPayoutInvoice?> GetByTokenAsync(Guid token, CancellationToken ct = default)
        => await _db.CollaboratorPayoutInvoices.FirstOrDefaultAsync(p => p.PublicToken == token, ct);

    public async Task<CollaboratorPayoutInvoice?> GetByCollaboratorAndPeriodAsync(
        Guid collaboratorId, string period, CancellationToken ct = default)
        => await _db.CollaboratorPayoutInvoices
            .FirstOrDefaultAsync(p => p.CollaboratorId == collaboratorId && p.Period == period, ct);

    public async Task<IReadOnlyList<CollaboratorPayoutInvoice>> ListByCollaboratorAsync(
        Guid collaboratorId, CancellationToken ct = default)
        => await _db.CollaboratorPayoutInvoices
            .Where(p => p.CollaboratorId == collaboratorId)
            .OrderByDescending(p => p.Period)
            .ToListAsync(ct);

    public async Task AddAsync(CollaboratorPayoutInvoice invoice, CancellationToken ct = default)
        => await _db.CollaboratorPayoutInvoices.AddAsync(invoice, ct);

    public Task UpdateAsync(CollaboratorPayoutInvoice invoice, CancellationToken ct = default)
    {
        _db.CollaboratorPayoutInvoices.Update(invoice);
        return Task.CompletedTask;
    }
}

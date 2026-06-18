using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class ProposalRepository : IProposalRepository
{
    private readonly ZentoryDbContext _db;

    public ProposalRepository(ZentoryDbContext db) => _db = db;

    public async Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Proposals.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Proposal?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await _db.Proposals
            .Include(p => p.Sections)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Proposal?> GetByPublicTokenAsync(Guid publicToken, CancellationToken ct = default)
        => await _db.Proposals
            .Include(p => p.Sections)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.PublicToken == publicToken && p.DeletedAt == null, ct);

    public async Task ReplaceSectionsAsync(
        Guid proposalId,
        IReadOnlyList<ProposalSection> sections,
        CancellationToken ct = default)
    {
        var existing = await _db.ProposalSections
            .Where(s => s.ProposalId == proposalId)
            .ToListAsync(ct);
        _db.ProposalSections.RemoveRange(existing);
        await _db.ProposalSections.AddRangeAsync(sections, ct);
    }

    public async Task ReplaceItemsAsync(
        Guid proposalId,
        IReadOnlyList<ProposalItem> items,
        CancellationToken ct = default)
    {
        var existing = await _db.ProposalItems
            .Where(i => i.ProposalId == proposalId)
            .ToListAsync(ct);
        _db.ProposalItems.RemoveRange(existing);
        await _db.ProposalItems.AddRangeAsync(items, ct);
    }

    public async Task<IReadOnlyList<Proposal>> ListAsync(
        Guid    organizationId,
        string? status   = null,
        Guid?   clientId = null,
        string? search   = null,
        CancellationToken ct = default)
    {
        var query = _db.Proposals
            .Where(p => p.OrganizationId == organizationId);

        if (status != null)
            query = query.Where(p => p.Status == status);

        if (clientId.HasValue)
            query = query.Where(p => p.ClientId == clientId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Title.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Proposal proposal, CancellationToken ct = default)
        => await _db.Proposals.AddAsync(proposal, ct);

    public Task UpdateAsync(Proposal proposal, CancellationToken ct = default)
    {
        _db.Proposals.Update(proposal);
        return Task.CompletedTask;
    }
}

using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class ClientRepository : IClientRepository
{
    private readonly ZentoryDbContext _db;

    public ClientRepository(ZentoryDbContext db) => _db = db;

    public async Task<Client?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Client>> ListAsync(
        Guid    organizationId,
        string? search = null,
        CancellationToken ct = default)
    {
        var query = _db.Clients
            .Where(c => c.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                c.ContactName.ToLower().Contains(term) ||
                (c.City != null && c.City.ToLower().Contains(term)));
        }

        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<int> CountAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Clients.CountAsync(c => c.OrganizationId == organizationId, ct);

    public async Task AddAsync(Client client, CancellationToken ct = default)
        => await _db.Clients.AddAsync(client, ct);

    public Task UpdateAsync(Client client, CancellationToken ct = default)
    {
        _db.Clients.Update(client);
        return Task.CompletedTask;
    }

    public async Task<bool> HasActiveProjectsOrPendingInvoicesAsync(
        Guid clientId,
        CancellationToken ct = default)
    {
        return await _db.Projects
            .AnyAsync(p => p.ClientId == clientId && p.Status == ProjectStatus.Active, ct);
    }
}

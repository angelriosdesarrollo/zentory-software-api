using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class OrganizationRepository : IOrganizationRepository
{
    private readonly ZentoryDbContext _db;

    public OrganizationRepository(ZentoryDbContext db) => _db = db;

    public async Task<Organization?> GetByIdAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Organizations.FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<Organization>> ListActiveByLegalTypeAsync(string legalType, CancellationToken ct = default)
        => await _db.Organizations
            .Where(o => o.LegalType == legalType && o.IsActive)
            .ToListAsync(ct);

    public async Task AddAsync(Organization organization, CancellationToken ct = default)
        => await _db.Organizations.AddAsync(organization, ct);
}

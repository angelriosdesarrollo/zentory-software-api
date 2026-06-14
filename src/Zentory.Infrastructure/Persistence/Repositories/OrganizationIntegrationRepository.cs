using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class OrganizationIntegrationRepository : IOrganizationIntegrationRepository
{
    private readonly ZentoryDbContext _db;

    public OrganizationIntegrationRepository(ZentoryDbContext db) => _db = db;

    public async Task<OrganizationIntegration?> GetByIntegrationIdAsync(
        Guid   organizationId,
        string integrationId,
        CancellationToken ct = default)
        => await _db.OrganizationIntegrations
            .FirstOrDefaultAsync(oi =>
                oi.OrganizationId == organizationId &&
                oi.IntegrationId  == integrationId, ct);

    public async Task AddAsync(OrganizationIntegration entity, CancellationToken ct = default)
        => await _db.OrganizationIntegrations.AddAsync(entity, ct);

    public Task UpdateAsync(OrganizationIntegration entity, CancellationToken ct = default)
    {
        _db.OrganizationIntegrations.Update(entity);
        return Task.CompletedTask;
    }
}

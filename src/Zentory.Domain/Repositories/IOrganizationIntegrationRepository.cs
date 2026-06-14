using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IOrganizationIntegrationRepository
{
    Task<OrganizationIntegration?> GetByIntegrationIdAsync(Guid organizationId, string integrationId, CancellationToken ct = default);
    Task AddAsync(OrganizationIntegration entity, CancellationToken ct = default);
    Task UpdateAsync(OrganizationIntegration entity, CancellationToken ct = default);
}

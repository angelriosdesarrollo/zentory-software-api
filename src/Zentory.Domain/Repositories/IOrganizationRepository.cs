using Zentory.Domain.Entities;

namespace Zentory.Domain.Repositories;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Organization organization, CancellationToken ct = default);
}

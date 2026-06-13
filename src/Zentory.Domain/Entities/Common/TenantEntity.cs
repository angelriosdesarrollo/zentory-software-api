namespace Zentory.Domain.Entities.Common;

public abstract class TenantEntity : BaseEntity
{
    public Guid OrganizationId { get; protected set; }
}

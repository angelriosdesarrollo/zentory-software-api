using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

public class AiQuotaOverride : BaseEntity
{
    public Guid     OrganizationId      { get; private set; }
    public Guid?    FeatureId           { get; private set; }  // NULL = aplica a todos los features
    public int?     MonthlyRequestLimit { get; private set; }  // NULL = ilimitado
    public long?    MonthlyTokenLimit   { get; private set; }  // NULL = ilimitado
    public DateTime ValidFrom           { get; private set; } = DateTime.UtcNow;
    public DateTime? ValidUntil         { get; private set; }
    public string?  Reason              { get; private set; }
    public string?  CreatedBy           { get; private set; }

    private AiQuotaOverride() { }

    public static AiQuotaOverride Create(
        Guid    organizationId,
        Guid?   featureId            = null,
        int?    monthlyRequestLimit  = null,
        long?   monthlyTokenLimit    = null,
        DateTime? validUntil         = null,
        string? reason               = null,
        string? createdBy            = null)
    {
        return new AiQuotaOverride
        {
            OrganizationId     = organizationId,
            FeatureId          = featureId,
            MonthlyRequestLimit= monthlyRequestLimit,
            MonthlyTokenLimit  = monthlyTokenLimit,
            ValidUntil         = validUntil,
            Reason             = reason,
            CreatedBy          = createdBy
        };
    }

    public bool IsActive() => ValidUntil is null || DateTime.UtcNow < ValidUntil;
}

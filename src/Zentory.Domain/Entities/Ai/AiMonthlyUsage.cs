using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

// Aggregated monthly usage per tenant + feature. Updated by background job.
// Plan-limit validation reads this table instead of querying usage_logs directly.
public class AiMonthlyUsage : BaseEntity
{
    public Guid    OrganizationId       { get; private set; }
    public Guid    FeatureId            { get; private set; }
    public short   Year                 { get; private set; }
    public short   Month                { get; private set; }

    public int     TotalRequests        { get; private set; }
    public int     SuccessfulRequests   { get; private set; }
    public long    TotalInputTokens     { get; private set; }
    public long    TotalOutputTokens    { get; private set; }
    public decimal TotalCostUsd         { get; private set; }
    public int     CacheHits            { get; private set; }

    public DateTime LastUpdatedAt       { get; private set; } = DateTime.UtcNow;

    private AiMonthlyUsage() { }

    public static AiMonthlyUsage Initialize(Guid organizationId, Guid featureId, short year, short month)
    {
        return new AiMonthlyUsage
        {
            OrganizationId = organizationId,
            FeatureId      = featureId,
            Year           = year,
            Month          = month
        };
    }

    public void Aggregate(int requests, int successful, long inputTokens, long outputTokens, decimal costUsd, int cacheHits)
    {
        TotalRequests      += requests;
        SuccessfulRequests += successful;
        TotalInputTokens   += inputTokens;
        TotalOutputTokens  += outputTokens;
        TotalCostUsd       += costUsd;
        CacheHits          += cacheHits;
        LastUpdatedAt       = DateTime.UtcNow;
    }
}

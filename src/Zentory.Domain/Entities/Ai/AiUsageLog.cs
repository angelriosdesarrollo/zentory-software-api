using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

// Partitioned by created_at (annual). Insert-only — source of truth for cost and latency.
public class AiUsageLog : BaseEntity
{
    public Guid     OrganizationId      { get; private set; }
    public Guid?    UserId              { get; private set; }
    public Guid     FeatureId           { get; private set; }
    public Guid     ModelId             { get; private set; }
    public Guid?    PromptTemplateId    { get; private set; }

    public string?  ContextType         { get; private set; }
    // 'proposal' | 'project' | 'invoice' | 'meeting' | 'report' | 'time_entry'
    public Guid?    ContextId           { get; private set; }

    public int      InputTokens         { get; private set; }
    public int      OutputTokens        { get; private set; }
    public decimal  CostUsd             { get; private set; }

    public int?     LatencyMs           { get; private set; }

    public string   Status              { get; private set; } = "success";
    // 'success' | 'error' | 'timeout' | 'rate_limited' | 'cached'
    public string?  ErrorCode           { get; private set; }

    public bool     ServedFromCache     { get; private set; }
    public string?  CacheKey            { get; private set; }  // SHA-256


    private AiUsageLog() { }

    public static AiUsageLog Create(
        Guid    organizationId,
        Guid    featureId,
        Guid    modelId,
        int     inputTokens,
        int     outputTokens,
        decimal costUsd,
        string  status          = "success",
        Guid?   userId          = null,
        Guid?   promptTemplateId= null,
        string? contextType     = null,
        Guid?   contextId       = null,
        int?    latencyMs       = null,
        bool    servedFromCache = false,
        string? cacheKey        = null,
        string? errorCode       = null)
    {
        return new AiUsageLog
        {
            OrganizationId   = organizationId,
            FeatureId        = featureId,
            ModelId          = modelId,
            InputTokens      = inputTokens,
            OutputTokens     = outputTokens,
            CostUsd          = costUsd,
            Status           = status,
            UserId           = userId,
            PromptTemplateId = promptTemplateId,
            ContextType      = contextType,
            ContextId        = contextId,
            LatencyMs        = latencyMs,
            ServedFromCache  = servedFromCache,
            CacheKey         = cacheKey,
            ErrorCode        = errorCode
        };
    }
}

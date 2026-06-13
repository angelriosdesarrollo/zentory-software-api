using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

public class AiModel : BaseEntity
{
    public Guid     ProviderId          { get; private set; }
    public string   ModelId             { get; private set; } = default!;
    // 'claude-sonnet-4-6' | 'claude-haiku-4-5-20251001' | 'gpt-4o'
    public string   DisplayName         { get; private set; } = default!;
    public int      ContextWindow       { get; private set; }
    public int      MaxOutputTokens     { get; private set; }
    public decimal  InputCostPer1k      { get; private set; }   // USD per 1,000 tokens
    public decimal  OutputCostPer1k     { get; private set; }
    public bool     SupportsStreaming   { get; private set; } = true;
    public bool     SupportsVision      { get; private set; }
    public bool     SupportsJsonMode    { get; private set; } = true;
    public bool     Active              { get; private set; } = true;
    public DateTime? DeprecatedAt       { get; private set; }

    private AiModel() { }

    public static AiModel Create(
        Guid    providerId,
        string  modelId,
        string  displayName,
        int     contextWindow,
        int     maxOutputTokens,
        decimal inputCostPer1k,
        decimal outputCostPer1k,
        bool    supportsVision   = false,
        bool    supportsStreaming = true,
        bool    supportsJsonMode = true)
    {
        return new AiModel
        {
            ProviderId        = providerId,
            ModelId           = modelId,
            DisplayName       = displayName,
            ContextWindow     = contextWindow,
            MaxOutputTokens   = maxOutputTokens,
            InputCostPer1k    = inputCostPer1k,
            OutputCostPer1k   = outputCostPer1k,
            SupportsVision    = supportsVision,
            SupportsStreaming  = supportsStreaming,
            SupportsJsonMode  = supportsJsonMode
        };
    }

    public void Deprecate() { DeprecatedAt = DateTime.UtcNow; Active = false; UpdatedAt = DateTime.UtcNow; }
}

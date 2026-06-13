using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

public class AiFeatureConfig : BaseEntity
{
    public Guid     FeatureId         { get; private set; }
    public Guid     ModelId           { get; private set; }
    public string   MinPlan           { get; private set; } = "pro";  // 'free' | 'pro' | 'studio'
    public string?  AccountType       { get; private set; }  // NULL = ambos | 'freelance' | 'empresa'
    public int      MaxInputTokens    { get; private set; } = 4000;
    public int      MaxOutputTokens   { get; private set; } = 2000;
    public decimal  Temperature       { get; private set; } = 0.70m;
    public string   ModelParams       { get; private set; } = "{}";  // JSONB as string
    public int?     MonthlyReqLimit   { get; private set; }
    public bool     IsActive          { get; private set; } = true;
    public DateTime ActivatedAt       { get; private set; } = DateTime.UtcNow;

    private AiFeatureConfig() { }

    public static AiFeatureConfig Create(
        Guid    featureId,
        Guid    modelId,
        string  minPlan        = "pro",
        int     maxInputTokens = 4000,
        int     maxOutputTokens= 2000,
        decimal temperature    = 0.70m,
        string? accountType    = null,
        int?    monthlyReqLimit= null)
    {
        return new AiFeatureConfig
        {
            FeatureId       = featureId,
            ModelId         = modelId,
            MinPlan         = minPlan,
            MaxInputTokens  = maxInputTokens,
            MaxOutputTokens = maxOutputTokens,
            Temperature     = temperature,
            AccountType     = accountType,
            MonthlyReqLimit = monthlyReqLimit
        };
    }

    public void Deactivate() { IsActive = false; }

    public void UpdateModel(Guid modelId, decimal temperature)
    {
        ModelId     = modelId;
        Temperature = temperature;
    }
}

namespace Zentory.Domain.Repositories;

/// <summary>Config activa de un feature de IA, ya resuelta con su modelo y prompt (join de las 4 tablas de ai.*).</summary>
public sealed record ActiveAiFeatureConfig(
    Guid    FeatureId,
    Guid    FeatureConfigId,
    string  MinPlan,
    int     MaxInputTokens,
    int     MaxOutputTokens,
    int?    MonthlyReqLimit,
    Guid    ModelId,
    string  ModelExternalId,
    decimal InputCostPer1k,
    decimal OutputCostPer1k,
    string  SystemPrompt);

public interface IAiFeatureConfigRepository
{
    /// <summary>Null si el feature no existe, está inactivo, o no tiene config/modelo/prompt activos.</summary>
    Task<ActiveAiFeatureConfig?> GetActiveByFeatureKeyAsync(string featureKey, CancellationToken ct = default);
}

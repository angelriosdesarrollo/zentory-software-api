using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class AiFeatureConfigRepository : IAiFeatureConfigRepository
{
    private readonly ZentoryDbContext _db;

    public AiFeatureConfigRepository(ZentoryDbContext db) => _db = db;

    public async Task<ActiveAiFeatureConfig?> GetActiveByFeatureKeyAsync(string featureKey, CancellationToken ct = default)
    {
        var row = await (
            from feature in _db.AiFeatures
            where feature.Key == featureKey && feature.Active
            join config in _db.AiFeatureConfigs.Where(c => c.IsActive) on feature.Id equals config.FeatureId
            join model in _db.AiModels on config.ModelId equals model.Id
            join prompt in _db.AiPromptTemplates.Where(p => p.IsActive) on feature.Id equals prompt.FeatureId
            select new
            {
                FeatureId       = feature.Id,
                FeatureConfigId = config.Id,
                config.MinPlan,
                config.MaxInputTokens,
                config.MaxOutputTokens,
                config.MonthlyReqLimit,
                ModelId         = model.Id,
                ModelExternalId = model.ModelId,
                model.InputCostPer1k,
                model.OutputCostPer1k,
                SystemPrompt    = prompt.SystemPrompt
            }
        ).FirstOrDefaultAsync(ct);

        return row is null ? null : new ActiveAiFeatureConfig(
            row.FeatureId, row.FeatureConfigId, row.MinPlan, row.MaxInputTokens, row.MaxOutputTokens,
            row.MonthlyReqLimit, row.ModelId, row.ModelExternalId, row.InputCostPer1k, row.OutputCostPer1k,
            row.SystemPrompt);
    }
}

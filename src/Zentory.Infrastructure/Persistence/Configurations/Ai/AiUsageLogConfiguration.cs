using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiUsageLogConfiguration : IEntityTypeConfiguration<AiUsageLog>
{
    public void Configure(EntityTypeBuilder<AiUsageLog> builder)
    {
        builder.ToTable("usage_logs", "ai");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(u => u.UserId).HasColumnName("user_id");
        builder.Property(u => u.FeatureId).HasColumnName("feature_id").IsRequired();
        builder.Property(u => u.ModelId).HasColumnName("model_id").IsRequired();
        builder.Property(u => u.PromptTemplateId).HasColumnName("prompt_template_id");
        builder.Property(u => u.ContextType).HasColumnName("context_type").HasMaxLength(50);
        builder.Property(u => u.ContextId).HasColumnName("context_id");
        builder.Property(u => u.InputTokens).HasColumnName("input_tokens").HasDefaultValue(0);
        builder.Property(u => u.OutputTokens).HasColumnName("output_tokens").HasDefaultValue(0);
        builder.Property(u => u.CostUsd).HasColumnName("cost_usd").HasColumnType("decimal(12,8)");
        builder.Property(u => u.LatencyMs).HasColumnName("latency_ms");
        builder.Property(u => u.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(u => u.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        builder.Property(u => u.ServedFromCache).HasColumnName("served_from_cache").HasDefaultValue(false);
        builder.Property(u => u.CacheKey).HasColumnName("cache_key").HasMaxLength(64);
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(u => new { u.OrganizationId, u.CreatedAt }).HasDatabaseName("ix_ai_usage_logs_org_created");
        builder.HasIndex(u => new { u.OrganizationId, u.FeatureId }).HasDatabaseName("ix_ai_usage_logs_org_feature");
    }
}

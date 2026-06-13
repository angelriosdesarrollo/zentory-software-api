using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiMonthlyUsageConfiguration : IEntityTypeConfiguration<AiMonthlyUsage>
{
    public void Configure(EntityTypeBuilder<AiMonthlyUsage> builder)
    {
        builder.ToTable("monthly_usage", "ai");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(m => m.FeatureId).HasColumnName("feature_id").IsRequired();
        builder.Property(m => m.Year).HasColumnName("year").IsRequired();
        builder.Property(m => m.Month).HasColumnName("month").IsRequired();
        builder.Property(m => m.TotalRequests).HasColumnName("total_requests").HasDefaultValue(0);
        builder.Property(m => m.SuccessfulRequests).HasColumnName("successful_requests").HasDefaultValue(0);
        builder.Property(m => m.TotalInputTokens).HasColumnName("total_input_tokens").HasDefaultValue(0L);
        builder.Property(m => m.TotalOutputTokens).HasColumnName("total_output_tokens").HasDefaultValue(0L);
        builder.Property(m => m.TotalCostUsd).HasColumnName("total_cost_usd").HasColumnType("decimal(12,8)");
        builder.Property(m => m.CacheHits).HasColumnName("cache_hits").HasDefaultValue(0);
        builder.Property(m => m.LastUpdatedAt).HasColumnName("last_updated_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(m => new { m.OrganizationId, m.FeatureId, m.Year, m.Month }).IsUnique().HasDatabaseName("ix_ai_monthly_usage_org_feature_period");
    }
}

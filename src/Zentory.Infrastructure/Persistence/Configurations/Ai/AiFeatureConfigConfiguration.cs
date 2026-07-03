using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiFeatureConfigConfiguration : IEntityTypeConfiguration<AiFeatureConfig>
{
    public void Configure(EntityTypeBuilder<AiFeatureConfig> builder)
    {
        builder.ToTable("feature_configs", "ai");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.FeatureId).HasColumnName("feature_id").IsRequired();
        builder.Property(c => c.ModelId).HasColumnName("model_id").IsRequired();
        builder.Property(c => c.MinPlan).HasColumnName("min_plan").HasMaxLength(20).IsRequired();
        builder.Property(c => c.LegalType).HasColumnName("legal_type").HasMaxLength(20);
        builder.Property(c => c.MaxInputTokens).HasColumnName("max_input_tokens").HasDefaultValue(4000);
        builder.Property(c => c.MaxOutputTokens).HasColumnName("max_output_tokens").HasDefaultValue(2000);
        builder.Property(c => c.Temperature).HasColumnName("temperature").HasColumnType("decimal(3,2)");
        builder.Property(c => c.ModelParams).HasColumnName("model_params").HasColumnType("jsonb");
        builder.Property(c => c.MonthlyReqLimit).HasColumnName("monthly_req_limit");
        builder.Property(c => c.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(c => c.ActivatedAt).HasColumnName("activated_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(c => c.FeatureId).IsUnique().HasDatabaseName("ix_ai_feature_configs_feature");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiModelConfiguration : IEntityTypeConfiguration<AiModel>
{
    public void Configure(EntityTypeBuilder<AiModel> builder)
    {
        builder.ToTable("models", "ai");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.ProviderId).HasColumnName("provider_id").IsRequired();
        builder.Property(m => m.ModelId).HasColumnName("model_id").HasMaxLength(100).IsRequired();
        builder.Property(m => m.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(m => m.ContextWindow).HasColumnName("context_window").IsRequired();
        builder.Property(m => m.MaxOutputTokens).HasColumnName("max_output_tokens").IsRequired();
        builder.Property(m => m.InputCostPer1k).HasColumnName("input_cost_per_1k").HasColumnType("decimal(10,8)").IsRequired();
        builder.Property(m => m.OutputCostPer1k).HasColumnName("output_cost_per_1k").HasColumnType("decimal(10,8)").IsRequired();
        builder.Property(m => m.SupportsStreaming).HasColumnName("supports_streaming").HasDefaultValue(true);
        builder.Property(m => m.SupportsVision).HasColumnName("supports_vision").HasDefaultValue(false);
        builder.Property(m => m.SupportsJsonMode).HasColumnName("supports_json_mode").HasDefaultValue(true);
        builder.Property(m => m.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(m => m.DeprecatedAt).HasColumnName("deprecated_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(m => m.ModelId).IsUnique().HasDatabaseName("ix_ai_models_model_id");
    }
}

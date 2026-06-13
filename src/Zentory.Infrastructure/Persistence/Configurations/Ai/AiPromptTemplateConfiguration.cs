using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiPromptTemplateConfiguration : IEntityTypeConfiguration<AiPromptTemplate>
{
    public void Configure(EntityTypeBuilder<AiPromptTemplate> builder)
    {
        builder.ToTable("prompt_templates", "ai");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.FeatureId).HasColumnName("feature_id").IsRequired();
        builder.Property(t => t.Version).HasColumnName("version").IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(t => t.SystemPrompt).HasColumnName("system_prompt").IsRequired();
        builder.Property(t => t.UserPromptTpl).HasColumnName("user_prompt_tpl");
        builder.Property(t => t.Variables).HasColumnName("variables").HasColumnType("jsonb");
        builder.Property(t => t.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(t => t.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(t => new { t.FeatureId, t.Version }).IsUnique().HasDatabaseName("ix_ai_prompt_templates_feature_version");
    }
}

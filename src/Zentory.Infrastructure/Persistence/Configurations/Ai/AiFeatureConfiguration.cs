using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiFeatureConfiguration : IEntityTypeConfiguration<AiFeature>
{
    public void Configure(EntityTypeBuilder<AiFeature> builder)
    {
        builder.ToTable("features", "ai");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        builder.Property(f => f.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(f => f.Description).HasColumnName("description");
        builder.Property(f => f.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        builder.Property(f => f.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(f => f.Key).IsUnique().HasDatabaseName("ix_ai_features_key");
    }
}

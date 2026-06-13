using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Ai;

namespace Zentory.Infrastructure.Persistence.Configurations.Ai;

public class AiProviderConfiguration : IEntityTypeConfiguration<AiProvider>
{
    public void Configure(EntityTypeBuilder<AiProvider> builder)
    {
        builder.ToTable("providers", "ai");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.BaseUrl).HasColumnName("base_url");
        builder.Property(p => p.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(p => p.Name).IsUnique().HasDatabaseName("ix_ai_providers_name");
    }
}

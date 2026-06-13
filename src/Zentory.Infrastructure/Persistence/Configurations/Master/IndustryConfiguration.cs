using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Master;

namespace Zentory.Infrastructure.Persistence.Configurations.Master;

public class IndustryConfiguration : IEntityTypeConfiguration<Industry>
{
    public void Configure(EntityTypeBuilder<Industry> builder)
    {
        builder.ToTable("industries", "master");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(i => i.Slug).HasColumnName("slug").HasMaxLength(50).IsRequired();
        builder.HasIndex(i => i.Slug).IsUnique().HasDatabaseName("ix_master_industries_slug");
    }
}

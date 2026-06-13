using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("organization_settings", "core");
        builder.HasKey(s => new { s.OrganizationId, s.Key });
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.Key).HasColumnName("key").HasMaxLength(100).IsRequired();
        builder.Property(s => s.Value).HasColumnName("value");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
    }
}

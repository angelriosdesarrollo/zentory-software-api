using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ServiceCatalogConfiguration : IEntityTypeConfiguration<ServiceCatalog>
{
    public void Configure(EntityTypeBuilder<ServiceCatalog> builder)
    {
        builder.ToTable("service_catalog", "core");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(s => s.Description).HasColumnName("description");
        builder.Property(s => s.UnitId).HasColumnName("unit_id");
        builder.Property(s => s.DefaultPrice).HasColumnName("default_price").HasColumnType("decimal(12,2)");
        builder.Property(s => s.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(s => s.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(s => s.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(s => s.OrganizationId).HasDatabaseName("ix_service_catalog_org_id");
    }
}

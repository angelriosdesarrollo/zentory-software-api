using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.OrganizationId);
        builder.Property(o => o.OrganizationId).HasColumnName("organization_id");

        builder.Property(o => o.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(o => o.LegalType).HasColumnName("legal_type").HasMaxLength(20).IsRequired();
        builder.Property(o => o.Country).HasColumnName("country").HasMaxLength(5).IsRequired()
            .HasDefaultValue("CO");
        builder.Property(o => o.OwnerId).HasColumnName("owner_id");
        builder.Property(o => o.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        // Borrado lógico y purga (PLAN-0018 RF-65, RF-78; M0c). El filtro global DeletedAt == null vive en
        // ZentoryDbContext junto a los demás filtros.
        builder.Property(o => o.DeletedAt).HasColumnName("deleted_at");
        builder.Property(o => o.DeletedBy).HasColumnName("deleted_by");
        builder.Property(o => o.PurgeStartedAt).HasColumnName("purge_started_at");
        builder.Property(o => o.ConcurrencyStamp).HasColumnName("concurrency_stamp").IsConcurrencyToken();

        builder.HasIndex(o => o.DeletedAt).HasDatabaseName("ix_organizations_deleted_at");
    }
}

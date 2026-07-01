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
        builder.Property(o => o.AccountType).HasColumnName("account_type").HasMaxLength(20).IsRequired();
        builder.Property(o => o.Country).HasColumnName("country").HasMaxLength(5).IsRequired()
            .HasDefaultValue("CO");
        builder.Property(o => o.OwnerId).HasColumnName("owner_id");
        builder.Property(o => o.IsActive).HasColumnName("is_active").HasDefaultValue(true);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("clients");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.ContactName).HasColumnName("contact_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(254);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(c => c.City).HasColumnName("city").HasMaxLength(100);
        builder.Property(c => c.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(c => c.Nit).HasColumnName("nit").HasMaxLength(30);
        builder.Property(c => c.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.OrganizationId).HasDatabaseName("ix_clients_org_id");
        builder.HasIndex(c => new { c.OrganizationId, c.Name }).HasDatabaseName("ix_clients_org_name");
    }
}

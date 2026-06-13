using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ClientContactConfiguration : IEntityTypeConfiguration<ClientContact>
{
    public void Configure(EntityTypeBuilder<ClientContact> builder)
    {
        builder.ToTable("client_contacts", "core");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(c => c.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(c => c.Phone).HasColumnName("phone").HasMaxLength(50);
        builder.Property(c => c.Role).HasColumnName("role").HasMaxLength(100);
        builder.Property(c => c.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(c => c.ClientId).HasDatabaseName("ix_client_contacts_client_id");
    }
}

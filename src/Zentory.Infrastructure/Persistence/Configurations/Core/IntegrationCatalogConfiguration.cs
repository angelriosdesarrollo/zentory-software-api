using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class IntegrationCatalogConfiguration : IEntityTypeConfiguration<IntegrationCatalog>
{
    public void Configure(EntityTypeBuilder<IntegrationCatalog> builder)
    {
        builder.ToTable("integration_catalog", "core");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasMaxLength(100);
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(i => i.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(i => i.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
        builder.Property(i => i.IsHidden).HasColumnName("is_hidden").HasDefaultValue(false);
        builder.Property(i => i.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(i => i.OrganizationIntegrations)
            .WithOne(oi => oi.Integration)
            .HasForeignKey(oi => oi.IntegrationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new { Id = "google-meet", Name = "Google Meet", Description = "Videollamadas integradas en propuestas y proyectos", IsEnabled = true, IsHidden = false, SortOrder = 1, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new { Id = "slack",       Name = "Slack",        Description = "Notificaciones de proyectos e invoices en tu workspace", IsEnabled = true, IsHidden = false, SortOrder = 2, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new { Id = "github",      Name = "GitHub",       Description = "Vincula repositorios con proyectos y time entries",      IsEnabled = true, IsHidden = false, SortOrder = 3, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new { Id = "figma",       Name = "Figma",        Description = "Adjunta entregables de diseño directamente en propuestas", IsEnabled = true, IsHidden = false, SortOrder = 4, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}

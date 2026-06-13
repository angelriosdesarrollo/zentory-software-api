using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "core");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(n => n.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(n => n.Title).HasColumnName("title").HasMaxLength(255).IsRequired();
        builder.Property(n => n.Body).HasColumnName("body");
        builder.Property(n => n.ActionUrl).HasColumnName("action_url");
        builder.Property(n => n.ReadAt).HasColumnName("read_at");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(n => new { n.UserId, n.ReadAt }).HasDatabaseName("ix_notifications_user_read");
    }
}

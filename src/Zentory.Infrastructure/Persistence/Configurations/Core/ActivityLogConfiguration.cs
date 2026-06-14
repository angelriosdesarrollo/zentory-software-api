using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs", "core");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(a => a.EntityType).HasColumnName("entity_type").HasMaxLength(50).IsRequired();
        builder.Property(a => a.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(a => a.EntityCode).HasColumnName("entity_code").HasMaxLength(50);
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.UserInitials).HasColumnName("user_initials").HasMaxLength(10).IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").IsRequired();
        builder.Property(a => a.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(a => new { a.OrganizationId, a.OccurredAt })
               .HasDatabaseName("ix_activity_logs_org_occurred");

        builder.HasIndex(a => new { a.OrganizationId, a.EntityType, a.EntityId })
               .HasDatabaseName("ix_activity_logs_org_entity");
    }
}

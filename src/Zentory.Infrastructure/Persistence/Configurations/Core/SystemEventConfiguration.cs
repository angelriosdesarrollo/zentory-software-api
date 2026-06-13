using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class SystemEventConfiguration : IEntityTypeConfiguration<SystemEvent>
{
    public void Configure(EntityTypeBuilder<SystemEvent> builder)
    {
        builder.ToTable("system_events", "core");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Processed).HasColumnName("processed").HasDefaultValue(false);
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message");
        builder.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue((short)0);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(e => new { e.OrganizationId, e.Processed }).HasDatabaseName("ix_system_events_org_processed");
    }
}

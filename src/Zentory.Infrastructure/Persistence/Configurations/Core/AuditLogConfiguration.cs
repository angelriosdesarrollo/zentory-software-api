using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log", "core");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.OrganizationId).HasColumnName("organization_id");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.TableName).HasColumnName("table_name").HasMaxLength(100).IsRequired();
        builder.Property(a => a.RecordId).HasColumnName("record_id").IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(10).IsRequired();
        builder.Property(a => a.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
        builder.Property(a => a.UserAgent).HasColumnName("user_agent");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(a => new { a.OrganizationId, a.CreatedAt }).HasDatabaseName("ix_audit_log_org_created");
    }
}

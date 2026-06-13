using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("time_entries", "core");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(t => t.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(t => t.CollaboratorId).HasColumnName("collaborator_id");
        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.Date).HasColumnName("date");
        builder.Property(t => t.Hours).HasColumnName("hours").HasColumnType("decimal(4,1)").IsRequired();
        builder.Property(t => t.RateCost).HasColumnName("rate_cost").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(t => t.RateBilled).HasColumnName("rate_billed").HasColumnType("decimal(10,2)");
        builder.Property(t => t.Billable).HasColumnName("billable").HasDefaultValue(true);
        builder.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(t => t.BilledAt).HasColumnName("billed_at");
        builder.Property(t => t.CreatedBy).HasColumnName("created_by");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(t => t.OrganizationId).HasDatabaseName("ix_time_entries_org_id");
        builder.HasIndex(t => t.ProjectId).HasDatabaseName("ix_time_entries_project_id");
        builder.HasIndex(t => new { t.OrganizationId, t.Date }).HasDatabaseName("ix_time_entries_org_date");
        builder.HasIndex(t => t.CollaboratorId).HasDatabaseName("ix_time_entries_collaborator_id");
    }
}

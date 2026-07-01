using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProjectTaskConfiguration : IEntityTypeConfiguration<ProjectTask>
{
    public void Configure(EntityTypeBuilder<ProjectTask> builder)
    {
        builder.ToTable("project_tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(t => t.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(t => t.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Priority).HasColumnName("priority").HasMaxLength(10).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.AssigneeId).HasColumnName("assignee_id");
        builder.Property(t => t.DueDate).HasColumnName("due_date");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");

        // Gantt fields
        builder.Property(t => t.MilestoneId).HasColumnName("milestone_id");
        builder.Property(t => t.StartDate).HasColumnName("start_date");
        builder.Property(t => t.Hours).HasColumnName("hours").HasDefaultValue(0);
        builder.Property(t => t.Dependencies).HasColumnName("dependencies").HasColumnType("text[]");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(t => t.ProjectId).HasDatabaseName("ix_project_tasks_project_id");
        builder.HasIndex(t => t.OrganizationId).HasDatabaseName("ix_project_tasks_org_id");
    }
}

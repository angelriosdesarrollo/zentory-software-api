using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProjectMilestoneConfiguration : IEntityTypeConfiguration<ProjectMilestone>
{
    public void Configure(EntityTypeBuilder<ProjectMilestone> builder)
    {
        builder.ToTable("project_milestones");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(m => m.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(m => m.Name).HasColumnName("name").HasMaxLength(300).IsRequired();
        builder.Property(m => m.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(m => m.Value).HasColumnName("value").HasColumnType("decimal(14,2)");
        builder.Property(m => m.DueDate).HasColumnName("due_date");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(m => m.ProjectId).HasDatabaseName("ix_project_milestones_project_id");
        builder.HasIndex(m => m.OrganizationId).HasDatabaseName("ix_project_milestones_org_id");
    }
}

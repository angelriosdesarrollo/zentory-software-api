using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProjectCollaboratorConfiguration : IEntityTypeConfiguration<ProjectCollaborator>
{
    public void Configure(EntityTypeBuilder<ProjectCollaborator> builder)
    {
        builder.ToTable("project_collaborators", "core");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(p => p.CollaboratorId).HasColumnName("collaborator_id").IsRequired();
        builder.Property(p => p.Role).HasColumnName("role").HasMaxLength(100);
        builder.Property(p => p.RateCost).HasColumnName("rate_cost").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.RateBilled).HasColumnName("rate_billed").HasColumnType("decimal(10,2)");
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.AssignedAt).HasColumnName("assigned_at");
        builder.Property(p => p.RemovedAt).HasColumnName("removed_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(p => new { p.ProjectId, p.CollaboratorId }).HasDatabaseName("ix_project_collaborators_project_collaborator");
    }
}

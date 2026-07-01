using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProjectShareConfiguration : IEntityTypeConfiguration<ProjectShare>
{
    public void Configure(EntityTypeBuilder<ProjectShare> builder)
    {
        builder.ToTable("project_shares");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.ProjectId).HasColumnName("project_id").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(s => s.Token).HasColumnName("token").HasMaxLength(64).IsRequired();
        builder.Property(s => s.Message).HasColumnName("message");
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at");
        builder.Property(s => s.IncludedFileIds).HasColumnName("included_file_ids").HasColumnType("text[]");
        builder.Property(s => s.IncludedDeliverableIds).HasColumnName("included_deliverable_ids").HasColumnType("text[]");
        builder.Property(s => s.DeletedAt).HasColumnName("deleted_at");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(s => s.Token).IsUnique().HasDatabaseName("ix_project_shares_token");
        builder.HasIndex(s => s.ProjectId).HasDatabaseName("ix_project_shares_project_id");
    }
}

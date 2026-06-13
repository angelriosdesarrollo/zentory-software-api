using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.ToTable("organization_members", "core");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(m => m.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        builder.Property(m => m.InvitedBy).HasColumnName("invited_by");
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at");
        builder.Property(m => m.DeletedAt).HasColumnName("deleted_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).HasDatabaseName("ix_org_members_org_user");
    }
}

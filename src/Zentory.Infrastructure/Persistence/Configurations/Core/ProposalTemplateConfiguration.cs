using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class ProposalTemplateConfiguration : IEntityTypeConfiguration<ProposalTemplate>
{
    public void Configure(EntityTypeBuilder<ProposalTemplate> builder)
    {
        builder.ToTable("proposal_templates", "core");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.Structure).HasColumnName("structure").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.IsDefault).HasColumnName("is_default").HasDefaultValue(false);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");
    }
}

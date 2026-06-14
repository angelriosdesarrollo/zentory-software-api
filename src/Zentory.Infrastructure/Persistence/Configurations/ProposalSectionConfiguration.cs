using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class ProposalSectionConfiguration : IEntityTypeConfiguration<ProposalSection>
{
    public void Configure(EntityTypeBuilder<ProposalSection> builder)
    {
        builder.ToTable("proposal_sections", "core");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.ProposalId).HasColumnName("proposal_id").IsRequired();
        builder.Property(s => s.SectionType).HasColumnName("section_type").HasMaxLength(50).IsRequired();
        builder.Property(s => s.Title).HasColumnName("title").HasMaxLength(255);
        builder.Property(s => s.Content).HasColumnName("content");
        builder.Property(s => s.SortOrder).HasColumnName("sort_order").HasDefaultValue((short)0);
        builder.Property(s => s.IsVisible).HasColumnName("is_visible").HasDefaultValue(true);
        builder.Property(s => s.IsEncrypted).HasColumnName("is_encrypted").HasDefaultValue(false);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(s => s.ProposalId).HasDatabaseName("ix_proposal_sections_proposal_id");
    }
}

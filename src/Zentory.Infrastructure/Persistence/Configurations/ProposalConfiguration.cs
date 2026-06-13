using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposals", "core");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(p => p.TemplateId).HasColumnName("template_id");
        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(p => p.PublicToken).HasColumnName("public_token").IsRequired();
        builder.Property(p => p.PublicTokenExpiresAt).HasColumnName("public_token_expires_at");
        builder.Property(p => p.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(12,2)");
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.IntroText).HasColumnName("intro_text");
        builder.Property(p => p.Conditions).HasColumnName("conditions");
        builder.Property(p => p.SentAt).HasColumnName("sent_at");
        builder.Property(p => p.FirstViewedAt).HasColumnName("first_viewed_at");
        builder.Property(p => p.LastViewedAt).HasColumnName("last_viewed_at");
        builder.Property(p => p.ViewCount).HasColumnName("view_count").HasDefaultValue(0);
        builder.Property(p => p.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(p => p.RejectedAt).HasColumnName("rejected_at");
        builder.Property(p => p.ExpiresAt).HasColumnName("expires_at");
        builder.Property(p => p.ConvertedToProjectId).HasColumnName("converted_to_project_id");
        builder.Property(p => p.ConvertedAt).HasColumnName("converted_at");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasMany(p => p.Sections)
               .WithOne()
               .HasForeignKey("ProposalId")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Items)
               .WithOne()
               .HasForeignKey("ProposalId")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.OrganizationId).HasDatabaseName("ix_proposals_org_id");
        builder.HasIndex(p => p.PublicToken).IsUnique().HasDatabaseName("ix_proposals_public_token");
        builder.HasIndex(p => p.ClientId).HasDatabaseName("ix_proposals_client_id");
    }
}

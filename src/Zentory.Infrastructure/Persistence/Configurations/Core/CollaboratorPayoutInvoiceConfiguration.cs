using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class CollaboratorPayoutInvoiceConfiguration : IEntityTypeConfiguration<CollaboratorPayoutInvoice>
{
    public void Configure(EntityTypeBuilder<CollaboratorPayoutInvoice> builder)
    {
        builder.ToTable("collaborator_payout_invoices", "core");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.CollaboratorId).HasColumnName("collaborator_id").IsRequired();
        builder.Property(p => p.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
        builder.Property(p => p.Concept).HasColumnName("concept").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(14,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(p => p.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(p => p.StorageKey).HasColumnName("storage_key");
        builder.Property(p => p.DocumentFileName).HasColumnName("document_file_name").HasMaxLength(255);
        builder.Property(p => p.DocumentFileSize).HasColumnName("document_file_size");
        builder.Property(p => p.DocumentContentType).HasColumnName("document_content_type").HasMaxLength(100);
        builder.Property(p => p.DeclaredAmount).HasColumnName("declared_amount").HasColumnType("numeric(14,2)");
        builder.Property(p => p.PublicToken).HasColumnName("public_token").IsRequired();
        builder.Property(p => p.TokenExpiresAt).HasColumnName("token_expires_at");
        builder.Property(p => p.GeneratedAt).HasColumnName("generated_at");
        builder.Property(p => p.SentAt).HasColumnName("sent_at");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(p => new { p.CollaboratorId, p.Period }).HasDatabaseName("ix_payout_invoices_collaborator_period");
        builder.HasIndex(p => p.PublicToken).IsUnique().HasDatabaseName("ix_payout_invoices_token");
    }
}

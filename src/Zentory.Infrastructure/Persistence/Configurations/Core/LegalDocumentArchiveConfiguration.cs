using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

// core.legal_document_archive (PLAN-0018 RF-79, M0d). Sin FK ni organization_id, y sin filtro global: no es de tenant.
public class LegalDocumentArchiveConfiguration : IEntityTypeConfiguration<LegalDocumentArchive>
{
    public void Configure(EntityTypeBuilder<LegalDocumentArchive> builder)
    {
        builder.ToTable("legal_document_archive", "core");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.SourceType).HasColumnName("source_type").HasMaxLength(50).IsRequired();
        builder.Property(a => a.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(a => a.DocumentNumber).HasColumnName("document_number").HasMaxLength(50).IsRequired();
        builder.Property(a => a.IssuedAt).HasColumnName("issued_at").IsRequired();
        builder.Property(a => a.IssuerTaxId).HasColumnName("issuer_tax_id").HasMaxLength(50).IsRequired();
        builder.Property(a => a.IssuerLegalName).HasColumnName("issuer_legal_name").HasMaxLength(300).IsRequired();
        builder.Property(a => a.DianCufe).HasColumnName("dian_cufe").HasMaxLength(96);
        builder.Property(a => a.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.PdfStorageKey).HasColumnName("pdf_storage_key").HasMaxLength(500);
        builder.Property(a => a.ArchivedAt).HasColumnName("archived_at").IsRequired();
        builder.Property(a => a.RetainUntil).HasColumnName("retain_until").IsRequired();

        // Idempotencia del archivo: un documento de origen se archiva una sola vez aunque la purga se reintente.
        builder.HasIndex(a => new { a.SourceType, a.SourceId }).IsUnique()
            .HasDatabaseName("ux_legal_document_archive_source");
        builder.HasIndex(a => a.RetainUntil).HasDatabaseName("ix_legal_document_archive_retain_until");
    }
}

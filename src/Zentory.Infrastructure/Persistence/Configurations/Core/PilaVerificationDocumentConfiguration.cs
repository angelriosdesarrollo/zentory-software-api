using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class PilaVerificationDocumentConfiguration : IEntityTypeConfiguration<PilaVerificationDocument>
{
    public void Configure(EntityTypeBuilder<PilaVerificationDocument> builder)
    {
        builder.ToTable("pila_verification_documents", "core");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.PilaVerificationId).HasColumnName("pila_verification_id").IsRequired();
        builder.Property(d => d.StorageKey).HasColumnName("storage_key").IsRequired();
        builder.Property(d => d.FileName).HasColumnName("file_name").HasMaxLength(255);
        builder.Property(d => d.FileSize).HasColumnName("file_size");
        builder.Property(d => d.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(d => d.UploadedAt).HasColumnName("uploaded_at");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(d => d.PilaVerificationId).HasDatabaseName("ix_pila_verification_documents_verification_id");
    }
}

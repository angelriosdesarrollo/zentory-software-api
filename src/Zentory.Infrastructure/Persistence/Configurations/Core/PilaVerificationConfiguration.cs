using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class PilaVerificationConfiguration : IEntityTypeConfiguration<PilaVerification>
{
    public void Configure(EntityTypeBuilder<PilaVerification> builder)
    {
        builder.ToTable("pila_verifications", "core");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.CollaboratorId).HasColumnName("collaborator_id").IsRequired();
        builder.Property(p => p.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(p => p.RequestedAt).HasColumnName("requested_at");
        builder.Property(p => p.ReceivedAt).HasColumnName("received_at");
        builder.Property(p => p.VerifiedAt).HasColumnName("verified_at");
        builder.Property(p => p.DocumentUrl).HasColumnName("document_url");
        builder.Property(p => p.DocumentFileName).HasColumnName("document_file_name").HasMaxLength(255);
        builder.Property(p => p.DocumentFileSize).HasColumnName("document_file_size");
        builder.Property(p => p.DocumentContentType).HasColumnName("document_content_type").HasMaxLength(100);
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.Token).HasColumnName("token").IsRequired();
        builder.Property(p => p.TokenExpiresAt).HasColumnName("token_expires_at");
        builder.HasIndex(p => new { p.CollaboratorId, p.Period }).IsUnique().HasDatabaseName("ix_pila_verifications_collaborator_period");
        builder.HasIndex(p => p.Token).IsUnique().HasDatabaseName("ix_pila_verifications_token");
    }
}

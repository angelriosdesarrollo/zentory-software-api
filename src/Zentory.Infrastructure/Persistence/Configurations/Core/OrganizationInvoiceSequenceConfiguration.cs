using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class OrganizationInvoiceSequenceConfiguration : IEntityTypeConfiguration<OrganizationInvoiceSequence>
{
    public void Configure(EntityTypeBuilder<OrganizationInvoiceSequence> builder)
    {
        builder.ToTable("organization_invoice_sequences", "core");
        builder.HasKey(s => new { s.OrganizationId, s.DocumentType, s.Year });
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.DocumentType).HasColumnName("document_type").HasMaxLength(20).IsRequired();
        builder.Property(s => s.Year).HasColumnName("year").IsRequired();
        builder.Property(s => s.Prefix).HasColumnName("prefix").HasMaxLength(10).IsRequired();
        builder.Property(s => s.LastNumber).HasColumnName("last_number").HasDefaultValue(0);
    }
}

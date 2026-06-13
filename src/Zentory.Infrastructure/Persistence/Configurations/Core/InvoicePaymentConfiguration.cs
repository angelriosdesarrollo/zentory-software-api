using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations.Core;

public class InvoicePaymentConfiguration : IEntityTypeConfiguration<InvoicePayment>
{
    public void Configure(EntityTypeBuilder<InvoicePayment> builder)
    {
        builder.ToTable("invoice_payments", "core");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("decimal(12,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.PaymentMethod).HasColumnName("payment_method").HasMaxLength(50);
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.Reference).HasColumnName("reference").HasMaxLength(200);
        builder.Property(p => p.Notes).HasColumnName("notes");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(p => p.InvoiceId).HasDatabaseName("ix_invoice_payments_invoice_id");
    }
}

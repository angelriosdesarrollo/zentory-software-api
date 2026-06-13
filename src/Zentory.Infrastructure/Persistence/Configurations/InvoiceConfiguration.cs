using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices", "core");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(i => i.ClientId).HasColumnName("client_id").IsRequired();
        builder.Property(i => i.ProjectId).HasColumnName("project_id");
        builder.Property(i => i.DocumentType).HasColumnName("document_type").HasMaxLength(20).IsRequired();
        builder.Property(i => i.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50).IsRequired();
        builder.Property(i => i.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(i => i.IssuedAt).HasColumnName("issued_at");
        builder.Property(i => i.DueAt).HasColumnName("due_at");
        builder.Property(i => i.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(12,2)");
        builder.Property(i => i.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(12,2)");
        builder.Property(i => i.Total).HasColumnName("total").HasColumnType("decimal(12,2)");
        builder.Property(i => i.AmountPaid).HasColumnName("amount_paid").HasColumnType("decimal(12,2)");
        builder.Property(i => i.AmountDue).HasColumnName("amount_due").HasColumnType("decimal(12,2)");
        builder.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(i => i.ExchangeRate).HasColumnName("exchange_rate").HasColumnType("decimal(16,8)");
        builder.Property(i => i.TotalBaseCurrency).HasColumnName("total_base_currency").HasColumnType("decimal(12,2)");
        builder.Property(i => i.DianCufe).HasColumnName("dian_cufe").HasMaxLength(96);
        builder.Property(i => i.DianStatus).HasColumnName("dian_status").HasMaxLength(20);
        builder.Property(i => i.DianSubmittedAt).HasColumnName("dian_submitted_at");
        builder.Property(i => i.DianResponse).HasColumnName("dian_response");
        builder.Property(i => i.Notes).HasColumnName("notes");
        builder.Property(i => i.PaymentTerms).HasColumnName("payment_terms");
        builder.Property(i => i.PaymentInstructions).HasColumnName("payment_instructions");
        builder.Property(i => i.SentAt).HasColumnName("sent_at");
        builder.Property(i => i.FirstViewedAt).HasColumnName("first_viewed_at");
        builder.Property(i => i.LastViewedAt).HasColumnName("last_viewed_at");
        builder.Property(i => i.ViewCount).HasColumnName("view_count").HasDefaultValue(0);
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
        builder.Property(i => i.DeletedAt).HasColumnName("deleted_at");

        builder.HasMany(i => i.Items)
               .WithOne()
               .HasForeignKey("InvoiceId")
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(i => i.Payments)
               .WithOne()
               .HasForeignKey("InvoiceId")
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.OrganizationId).HasDatabaseName("ix_invoices_org_id");
        builder.HasIndex(i => new { i.OrganizationId, i.InvoiceNumber }).IsUnique().HasDatabaseName("ix_invoices_org_number");
        builder.HasIndex(i => new { i.OrganizationId, i.Status }).HasDatabaseName("ix_invoices_org_status");
        builder.HasIndex(i => i.ClientId).HasDatabaseName("ix_invoices_client_id");
    }
}

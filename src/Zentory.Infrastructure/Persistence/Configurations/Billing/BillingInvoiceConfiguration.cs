using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class BillingInvoiceConfiguration : IEntityTypeConfiguration<BillingInvoice>
{
    public void Configure(EntityTypeBuilder<BillingInvoice> builder)
    {
        builder.ToTable("invoices", "billing");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(i => i.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(i => i.SubscriptionId).HasColumnName("subscription_id");
        builder.Property(i => i.GatewayInvoiceId).HasColumnName("gateway_invoice_id").HasMaxLength(100);
        builder.Property(i => i.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50);
        builder.Property(i => i.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(i => i.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(i => i.PeriodStart).HasColumnName("period_start");
        builder.Property(i => i.PeriodEnd).HasColumnName("period_end");
        builder.Property(i => i.DueAt).HasColumnName("due_at");
        builder.Property(i => i.PaidAt).HasColumnName("paid_at");
        builder.Property(i => i.HostedInvoiceUrl).HasColumnName("hosted_invoice_url");
        builder.Property(i => i.InvoicePdf).HasColumnName("invoice_pdf");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at");
    }
}

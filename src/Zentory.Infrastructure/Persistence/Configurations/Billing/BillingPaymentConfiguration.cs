using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Infrastructure.Persistence.Configurations.Billing;

public class BillingPaymentConfiguration : IEntityTypeConfiguration<BillingPayment>
{
    public void Configure(EntityTypeBuilder<BillingPayment> builder)
    {
        builder.ToTable("payments", "billing");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(p => p.InvoiceId).HasColumnName("invoice_id");
        builder.Property(p => p.PaymentMethodId).HasColumnName("payment_method_id");
        builder.Property(p => p.GatewayTransactionId).HasColumnName("gateway_transaction_id").HasMaxLength(100);
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(p => p.FailureReason).HasColumnName("failure_reason");
        builder.Property(p => p.PaidAt).HasColumnName("paid_at");
        builder.Property(p => p.RefundedAt).HasColumnName("refunded_at");
        builder.Property(p => p.RefundAmount).HasColumnName("refund_amount").HasColumnType("decimal(10,2)");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
    }
}

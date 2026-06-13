using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class BillingPayment : BaseEntity
{
    public Guid     OrganizationId        { get; private set; }
    public Guid?    InvoiceId             { get; private set; }
    public Guid?    PaymentMethodId       { get; private set; }

    public string?  GatewayTransactionId  { get; private set; }  // pi_xxx en Stripe
    public decimal  Amount                { get; private set; }
    public string   Currency              { get; private set; } = default!;
    public string   Status                { get; private set; } = default!;
    // 'succeeded' | 'failed' | 'refunded' | 'partially_refunded'
    public string?  FailureReason         { get; private set; }

    public DateTime? PaidAt               { get; private set; }
    public DateTime? RefundedAt           { get; private set; }
    public decimal?  RefundAmount         { get; private set; }


    private BillingPayment() { }

    public static BillingPayment Create(
        Guid    organizationId,
        decimal amount,
        string  currency,
        string  status,
        string? gatewayTransactionId = null,
        Guid?   invoiceId            = null,
        Guid?   paymentMethodId      = null,
        DateTime? paidAt             = null,
        string? failureReason        = null)
    {
        return new BillingPayment
        {
            OrganizationId       = organizationId,
            Amount               = amount,
            Currency             = currency,
            Status               = status,
            GatewayTransactionId = gatewayTransactionId,
            InvoiceId            = invoiceId,
            PaymentMethodId      = paymentMethodId,
            PaidAt               = paidAt,
            FailureReason        = failureReason
        };
    }

    public void MarkRefunded(decimal refundAmount)
    {
        Status        = refundAmount >= Amount ? "refunded" : "partially_refunded";
        RefundedAt    = DateTime.UtcNow;
        RefundAmount  = refundAmount;
    }
}

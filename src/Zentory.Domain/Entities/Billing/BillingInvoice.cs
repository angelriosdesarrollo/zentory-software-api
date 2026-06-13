using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

// Facturas que Zentory emite al tenant — distintas de core.invoices (las del tenant a sus clientes).
public class BillingInvoice : BaseEntity
{
    public Guid     OrganizationId    { get; private set; }
    public Guid     CustomerId        { get; private set; }
    public Guid?    SubscriptionId    { get; private set; }

    public string?  GatewayInvoiceId  { get; private set; }  // in_xxx en Stripe
    public string?  InvoiceNumber     { get; private set; }
    public string   Status            { get; private set; } = "draft";
    // 'draft' | 'open' | 'paid' | 'void' | 'uncollectible'

    public decimal  Amount            { get; private set; }
    public string   Currency          { get; private set; } = "USD";
    public DateTime? PeriodStart      { get; private set; }
    public DateTime? PeriodEnd        { get; private set; }
    public DateTime? DueAt            { get; private set; }
    public DateTime? PaidAt           { get; private set; }
    public string?  HostedInvoiceUrl  { get; private set; }
    public string?  InvoicePdf        { get; private set; }


    private BillingInvoice() { }

    public static BillingInvoice Create(
        Guid    organizationId,
        Guid    customerId,
        decimal amount,
        string  currency       = "USD",
        Guid?   subscriptionId = null)
    {
        return new BillingInvoice
        {
            OrganizationId = organizationId,
            CustomerId     = customerId,
            SubscriptionId = subscriptionId,
            Amount         = amount,
            Currency       = currency
        };
    }

    public void MarkPaid(DateTime paidAt)
    {
        Status    = "paid";
        PaidAt    = paidAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetGatewayData(string gatewayInvoiceId, string? hostedUrl, string? pdfUrl)
    {
        GatewayInvoiceId = gatewayInvoiceId;
        HostedInvoiceUrl = hostedUrl;
        InvoicePdf       = pdfUrl;
        UpdatedAt        = DateTime.UtcNow;
    }
}

using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class BillingCredit : BaseEntity
{
    public Guid     OrganizationId       { get; private set; }
    public decimal  Amount               { get; private set; }
    public string   Currency             { get; private set; } = "USD";
    public string   Type                 { get; private set; } = default!;
    // 'promotional' | 'referral' | 'refund' | 'adjustment'
    public string?  Reason               { get; private set; }
    public DateTime? ExpiresAt           { get; private set; }
    public DateTime? AppliedAt           { get; private set; }
    public Guid?    AppliedToInvoiceId   { get; private set; }

    private BillingCredit() { }

    public static BillingCredit Create(
        Guid    organizationId,
        decimal amount,
        string  type,
        string  currency      = "USD",
        string? reason        = null,
        DateTime? expiresAt   = null)
    {
        return new BillingCredit
        {
            OrganizationId = organizationId,
            Amount         = amount,
            Type           = type,
            Currency       = currency,
            Reason         = reason,
            ExpiresAt      = expiresAt
        };
    }

    public void Apply(Guid invoiceId)
    {
        AppliedToInvoiceId = invoiceId;
        AppliedAt          = DateTime.UtcNow;
    }
}

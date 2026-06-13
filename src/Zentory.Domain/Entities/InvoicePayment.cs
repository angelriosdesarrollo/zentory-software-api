using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class InvoicePayment : BaseEntity
{
    public Guid     OrganizationId { get; private set; }
    public Guid     InvoiceId      { get; private set; }
    public decimal  Amount         { get; private set; }
    public string   Currency       { get; private set; } = default!;
    public string?  PaymentMethod  { get; private set; }
    // 'transfer'|'cash'|'card'|'nequi'|'daviplata'|'pse'
    public DateOnly PaidAt         { get; private set; }
    public string?  Reference      { get; private set; }  // nro de transferencia
    public string?  Notes          { get; private set; }
    public Guid?    CreatedBy      { get; private set; }

    private InvoicePayment() { }

    public static InvoicePayment Create(
        Guid    organizationId,
        Guid    invoiceId,
        decimal amount,
        string  currency,
        DateOnly paidAt,
        string? paymentMethod = null,
        string? reference     = null,
        string? notes         = null,
        Guid?   createdBy     = null)
    {
        return new InvoicePayment
        {
            OrganizationId = organizationId,
            InvoiceId      = invoiceId,
            Amount         = amount,
            Currency       = currency,
            PaidAt         = paidAt,
            PaymentMethod  = paymentMethod,
            Reference      = reference,
            Notes          = notes,
            CreatedBy      = createdBy
        };
    }
}

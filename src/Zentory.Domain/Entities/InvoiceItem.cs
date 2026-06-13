using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class InvoiceItem : BaseEntity
{
    public Guid    OrganizationId { get; private set; }
    public Guid    InvoiceId      { get; private set; }
    public Guid?   TimeEntryId    { get; private set; }
    public Guid?   ServiceId      { get; private set; }

    public string  Description    { get; private set; } = default!;
    public decimal Quantity       { get; private set; } = 1;
    public short?  UnitId         { get; private set; }
    public decimal UnitPrice      { get; private set; }
    public short?  TaxTypeId      { get; private set; }
    public decimal DiscountPct    { get; private set; }
    public decimal Total          { get; private set; }  // computed: qty * unit_price * (1 - disc/100)
    public short   SortOrder      { get; private set; }

    private InvoiceItem() { }

    public static InvoiceItem Create(
        Guid    organizationId,
        Guid    invoiceId,
        string  description,
        decimal quantity,
        decimal unitPrice,
        short   sortOrder    = 0,
        decimal discountPct  = 0,
        Guid?   timeEntryId  = null,
        Guid?   serviceId    = null,
        short?  taxTypeId    = null,
        short?  unitId       = null)
    {
        return new InvoiceItem
        {
            OrganizationId = organizationId,
            InvoiceId      = invoiceId,
            Description    = description,
            Quantity       = quantity,
            UnitPrice      = unitPrice,
            DiscountPct    = discountPct,
            Total          = Math.Round(quantity * unitPrice * (1m - discountPct / 100m), 2),
            SortOrder      = sortOrder,
            TimeEntryId    = timeEntryId,
            ServiceId      = serviceId,
            TaxTypeId      = taxTypeId,
            UnitId         = unitId
        };
    }
}

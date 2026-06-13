using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProposalItem : BaseEntity
{
    public Guid    OrganizationId { get; private set; }
    public Guid    ProposalId     { get; private set; }
    public Guid?   ServiceId      { get; private set; }
    public string  Description    { get; private set; } = default!;
    public decimal Quantity       { get; private set; } = 1;
    public short?  UnitId         { get; private set; }
    public decimal UnitPrice      { get; private set; }
    public decimal DiscountPct    { get; private set; }
    public decimal Total          { get; private set; }  // qty * unit_price * (1 - discount/100)
    public short   SortOrder      { get; private set; }

    private ProposalItem() { }

    public static ProposalItem Create(
        Guid    organizationId,
        Guid    proposalId,
        string  description,
        decimal quantity,
        decimal unitPrice,
        short   sortOrder   = 0,
        decimal discountPct = 0,
        Guid?   serviceId   = null,
        short?  unitId      = null)
    {
        return new ProposalItem
        {
            OrganizationId = organizationId,
            ProposalId     = proposalId,
            Description    = description,
            Quantity       = quantity,
            UnitPrice      = unitPrice,
            DiscountPct    = discountPct,
            Total          = Math.Round(quantity * unitPrice * (1m - discountPct / 100m), 2),
            SortOrder      = sortOrder,
            ServiceId      = serviceId,
            UnitId         = unitId
        };
    }

    public void Update(string description, decimal quantity, decimal unitPrice, decimal discountPct, short sortOrder)
    {
        Description = description;
        Quantity    = quantity;
        UnitPrice   = unitPrice;
        DiscountPct = discountPct;
        Total       = Math.Round(quantity * unitPrice * (1m - discountPct / 100m), 2);
        SortOrder   = sortOrder;
        Touch();
    }
}

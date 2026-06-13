using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class CouponCode : BaseEntity
{
    public string   Code                { get; private set; } = default!;
    public string?  Description         { get; private set; }
    public string   DiscountType        { get; private set; } = default!;  // 'pct' | 'fixed'
    public decimal  DiscountValue       { get; private set; }
    public string?  Currency            { get; private set; }  // NULL si pct
    public string?  AppliesToPlan       { get; private set; }  // NULL = todos los planes
    public int?     MaxRedemptions      { get; private set; }  // NULL = ilimitado
    public int      CurrentRedemptions  { get; private set; }
    public DateTime ValidFrom           { get; private set; } = DateTime.UtcNow;
    public DateTime? ValidUntil         { get; private set; }
    public bool     Active              { get; private set; } = true;

    private CouponCode() { }

    public static CouponCode Create(
        string  code,
        string  discountType,
        decimal discountValue,
        string? currency       = null,
        string? appliesToPlan  = null,
        int?    maxRedemptions = null,
        DateTime? validUntil   = null,
        string? description    = null)
    {
        return new CouponCode
        {
            Code            = code,
            DiscountType    = discountType,
            DiscountValue   = discountValue,
            Currency        = currency,
            AppliesToPlan   = appliesToPlan,
            MaxRedemptions  = maxRedemptions,
            ValidUntil      = validUntil,
            Description     = description
        };
    }

    public bool CanRedeem() =>
        Active &&
        (ValidUntil is null || DateTime.UtcNow < ValidUntil) &&
        (MaxRedemptions is null || CurrentRedemptions < MaxRedemptions);

    public void Redeem() { CurrentRedemptions++; }
    public void Deactivate() { Active = false; }
}

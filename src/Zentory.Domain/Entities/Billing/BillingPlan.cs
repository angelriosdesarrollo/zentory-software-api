using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class BillingPlan : BaseEntity
{
    public string   Name                  { get; private set; } = default!;  // 'free' | 'pro' | 'studio'
    public string   DisplayName           { get; private set; } = default!;
    public decimal  PriceMonthlyUsd       { get; private set; }
    public decimal  PriceAnnualUsd        { get; private set; }
    public string?  GatewayPriceIdMonthly { get; private set; }
    public string?  GatewayPriceIdAnnual  { get; private set; }
    public bool     Active                { get; private set; } = true;
    public short    SortOrder             { get; private set; }

    private BillingPlan() { }

    public static BillingPlan Create(
        string  name,
        string  displayName,
        decimal priceMonthlyUsd,
        decimal priceAnnualUsd,
        short   sortOrder = 0)
    {
        return new BillingPlan
        {
            Name            = name,
            DisplayName     = displayName,
            PriceMonthlyUsd = priceMonthlyUsd,
            PriceAnnualUsd  = priceAnnualUsd,
            SortOrder       = sortOrder
        };
    }

    public void SetGatewayPriceIds(string? monthly, string? annual)
    {
        GatewayPriceIdMonthly = monthly;
        GatewayPriceIdAnnual  = annual;
    }
}

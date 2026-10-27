using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class CouponRedemption : BaseEntity
{
    public Guid     CouponId        { get; private set; }
    // Nullable desde M0c (PLAN-0018 C-6, RF-78): es facturación del owner y la purga de la organización la desvincula.
    public Guid?     OrganizationId  { get; private set; }
    public Guid?    SubscriptionId  { get; private set; }
    public decimal? DiscountAmount  { get; private set; }
    public DateTime RedeemedAt      { get; private set; } = DateTime.UtcNow;

    private CouponRedemption() { }

    public static CouponRedemption Create(
        Guid    couponId,
        Guid    organizationId,
        Guid?   subscriptionId = null,
        decimal? discountAmount = null)
    {
        return new CouponRedemption
        {
            CouponId       = couponId,
            OrganizationId = organizationId,
            SubscriptionId = subscriptionId,
            DiscountAmount = discountAmount
        };
    }
}

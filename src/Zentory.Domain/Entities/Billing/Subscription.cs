using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class Subscription : BaseEntity
{
    public Guid     OrganizationId         { get; private set; }
    public Guid     CustomerId             { get; private set; }
    public Guid     PlanId                 { get; private set; }

    public string?  GatewaySubscriptionId  { get; private set; }  // sub_xxx en Stripe
    public string?  GatewayPriceId         { get; private set; }

    public string   Status                 { get; private set; } = "trialing";
    // 'trialing' | 'active' | 'past_due' | 'cancelled' | 'unpaid' | 'incomplete'
    public string   BillingPeriod          { get; private set; } = "monthly";
    // 'monthly' | 'annual'

    public DateTime? CurrentPeriodStart    { get; private set; }
    public DateTime? CurrentPeriodEnd      { get; private set; }
    public DateTime? TrialEndsAt           { get; private set; }

    public bool     CancelAtPeriodEnd      { get; private set; }
    public DateTime? CancelledAt           { get; private set; }
    public string?  CancellationReason     { get; private set; }

    public decimal? Amount                 { get; private set; }
    public string   Currency               { get; private set; } = "USD";
    public decimal  DiscountPct            { get; private set; }

    public short    DunningAttempt         { get; private set; }
    public DateTime? NextDunningAt         { get; private set; }


    private Subscription() { }

    public static Subscription Create(
        Guid   organizationId,
        Guid   customerId,
        Guid   planId,
        string billingPeriod = "monthly",
        DateTime? trialEndsAt = null)
    {
        return new Subscription
        {
            OrganizationId = organizationId,
            CustomerId     = customerId,
            PlanId         = planId,
            BillingPeriod  = billingPeriod,
            TrialEndsAt    = trialEndsAt
        };
    }

    public void Activate(string gatewaySubId, DateTime periodStart, DateTime periodEnd)
    {
        Status                = "active";
        GatewaySubscriptionId = gatewaySubId;
        CurrentPeriodStart    = periodStart;
        CurrentPeriodEnd      = periodEnd;
        UpdatedAt             = DateTime.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        Status               = "cancelled";
        CancelledAt          = DateTime.UtcNow;
        CancellationReason   = reason;
        CancelAtPeriodEnd    = false;
        UpdatedAt            = DateTime.UtcNow;
    }

    public void ScheduleCancellation() { CancelAtPeriodEnd = true; UpdatedAt = DateTime.UtcNow; }

    public void RecordDunning(DateTime? nextAttempt)
    {
        Status         = "past_due";
        DunningAttempt++;
        NextDunningAt  = nextAttempt;
        UpdatedAt      = DateTime.UtcNow;
    }
}

using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class PlanMarketing : BaseEntity
{
    public Guid     PlanId          { get; private set; }
    public string   AccountType     { get; private set; } = default!;  // 'freelance' | 'empresa'
    public string   Tagline         { get; private set; } = default!;
    public string   CtaText         { get; private set; } = default!;
    public bool     IsPopular       { get; private set; }
    public string?  FeaturesHeading { get; private set; }

    private PlanMarketing() { }

    public static PlanMarketing Create(
        Guid    planId,
        string  accountType,
        string  tagline,
        string  ctaText,
        bool    isPopular        = false,
        string? featuresHeading  = null)
    {
        return new PlanMarketing
        {
            PlanId          = planId,
            AccountType     = accountType,
            Tagline         = tagline,
            CtaText         = ctaText,
            IsPopular       = isPopular,
            FeaturesHeading = featuresHeading
        };
    }

    public void Update(string tagline, string ctaText, bool isPopular, string? featuresHeading)
    {
        Tagline         = tagline;
        CtaText         = ctaText;
        IsPopular       = isPopular;
        FeaturesHeading = featuresHeading;
        UpdatedAt       = DateTime.UtcNow;
    }
}

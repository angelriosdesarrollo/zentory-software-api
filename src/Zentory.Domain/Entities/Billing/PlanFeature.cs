using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class PlanFeature : BaseEntity
{
    public Guid    PlanId      { get; private set; }
    public string  AccountType { get; private set; } = default!;  // 'freelance' | 'empresa'
    public string  Text        { get; private set; } = default!;
    public bool    IsHighlight { get; private set; }
    public string? BadgeText   { get; private set; }
    public short   SortOrder   { get; private set; }

    private PlanFeature() { }

    public static PlanFeature Create(
        Guid   planId,
        string accountType,
        string text,
        bool   isHighlight = false,
        string? badgeText  = null,
        short  sortOrder   = 0)
    {
        return new PlanFeature
        {
            PlanId      = planId,
            AccountType = accountType,
            Text        = text,
            IsHighlight = isHighlight,
            BadgeText   = badgeText,
            SortOrder   = sortOrder
        };
    }
}

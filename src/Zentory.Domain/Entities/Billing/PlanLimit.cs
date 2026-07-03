using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class PlanLimit : BaseEntity
{
    public Guid    PlanId      { get; private set; }
    public string  LegalType { get; private set; } = default!;  // 'freelance' | 'empresa'
    public string  FeatureKey  { get; private set; } = default!;
    // 'max_clients' | 'max_projects' | 'max_invoices_month' | 'max_collaborators' | ...
    public int?    LimitValue  { get; private set; }  // NULL = ilimitado

    private PlanLimit() { }

    public static PlanLimit Create(Guid planId, string legalType, string featureKey, int? limitValue)
    {
        return new PlanLimit
        {
            PlanId      = planId,
            LegalType = legalType,
            FeatureKey  = featureKey,
            LimitValue  = limitValue
        };
    }

    public bool IsUnlimited => LimitValue is null;
}

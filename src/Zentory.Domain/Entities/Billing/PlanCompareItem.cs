using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Billing;

public class PlanCompareItem : BaseEntity
{
    public string  FeatureName    { get; private set; } = default!;
    public string? AccountType    { get; private set; }  // NULL = ambos | 'freelance' | 'empresa'
    public bool    IsEmpresaOnly  { get; private set; }
    public string? FreeValue      { get; private set; }   // NULL=✗, 'true'=✓, text=literal
    public string? ProValue       { get; private set; }
    public string? StudioValue    { get; private set; }
    public short   SortOrder      { get; private set; }

    private PlanCompareItem() { }

    public static PlanCompareItem Create(
        string  featureName,
        string? freeValue,
        string? proValue,
        string? studioValue,
        short   sortOrder      = 0,
        string? accountType    = null,
        bool    isEmpresaOnly  = false)
    {
        return new PlanCompareItem
        {
            FeatureName   = featureName,
            FreeValue     = freeValue,
            ProValue      = proValue,
            StudioValue   = studioValue,
            SortOrder     = sortOrder,
            AccountType   = accountType,
            IsEmpresaOnly = isEmpresaOnly
        };
    }
}

namespace Zentory.Domain.Entities;

// PK = ProjectId. Updated by trigger or background job — never by handlers directly.
public class ProjectFinancials
{
    public Guid     ProjectId            { get; private set; }

    public decimal  ContractValue        { get; private set; }
    public decimal  TotalInvoiced        { get; private set; }
    public decimal  TotalCollected       { get; private set; }

    public decimal  TotalCost            { get; private set; }
    public decimal  BillableAmount       { get; private set; }

    public decimal  TotalHours           { get; private set; }
    public decimal  BillableHours        { get; private set; }
    public decimal  BudgetHours          { get; private set; }

    public decimal? GrossMargin          { get; private set; }
    public decimal? GrossMarginPct       { get; private set; }
    public decimal? NetMargin            { get; private set; }

    public decimal? PctTimeElapsed       { get; private set; }
    public decimal? PctHoursConsumed     { get; private set; }
    public decimal? PctPaymentsReceived  { get; private set; }

    public string   Currency             { get; private set; } = "COP";
    public DateTime LastCalculatedAt     { get; private set; } = DateTime.UtcNow;

    private ProjectFinancials() { }

    public static ProjectFinancials Initialize(Guid projectId, string currency = "COP")
    {
        return new ProjectFinancials { ProjectId = projectId, Currency = currency };
    }
}

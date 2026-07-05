using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public enum ProjectStatus { Active, Paused, Completed, Cancelled }
public enum BillingType   { Hourly, Milestone, FixedPrice }

public class Project : TenantEntity
{
    public string        Name           { get; private set; } = default!;
    public Guid          ClientId       { get; private set; }
    public ProjectStatus Status         { get; private set; } = ProjectStatus.Active;
    public BillingType   BillingType    { get; private set; }
    public decimal       ContractValue  { get; private set; }
    public string        Currency       { get; private set; } = "USD";
    public int           HoursTotal     { get; private set; }
    public int           HoursUsed      { get; private set; }
    public DateTime?     StartDate      { get; private set; }
    public DateTime?     EndDate        { get; private set; }
    public Guid?         ProposalId     { get; private set; }
    public string?       Type           { get; private set; }
    // "software" | "obra_publica" | "consultoria" | "marketing" | "otro" — Zentory.Domain.Constants.WorkType
    public bool          IsDeleted      { get; private set; }

    private Project() { }

    public static Project Create(
        Guid         organizationId,
        Guid         clientId,
        string       name,
        BillingType  billingType,
        decimal      contractValue,
        string       currency     = "USD",
        int          hoursTotal   = 0,
        DateTime?    startDate    = null,
        DateTime?    endDate      = null,
        Guid?        proposalId   = null,
        string?      type         = null,
        Guid?        id           = null)
    {
        return new Project
        {
            Id             = id ?? Guid.NewGuid(),
            OrganizationId = organizationId,
            ClientId       = clientId,
            Name           = name,
            BillingType    = billingType,
            ContractValue  = contractValue,
            Currency       = currency,
            HoursTotal     = hoursTotal,
            StartDate      = startDate,
            EndDate        = endDate,
            ProposalId     = proposalId,
            Type           = type
        };
    }

    public void Update(
        string      name,
        BillingType billingType,
        decimal     contractValue,
        string      currency,
        int         hoursTotal,
        DateTime?   startDate,
        DateTime?   endDate,
        string?     type)
    {
        Name          = name;
        BillingType   = billingType;
        ContractValue = contractValue;
        Currency      = currency;
        HoursTotal    = hoursTotal;
        StartDate     = startDate;
        EndDate       = endDate;
        Type          = type;
        Touch();
    }

    public void LogHours(int hours)
    {
        HoursUsed += hours;
        Touch();
    }

    public void ChangeStatus(ProjectStatus newStatus)
    {
        Status = newStatus;
        Touch();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Touch();
    }
}

using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class TimeEntry : TenantEntity
{
    public Guid      ProjectId      { get; private set; }
    public Guid?     CollaboratorId { get; private set; }

    public string?   Description    { get; private set; }
    public DateOnly  Date           { get; private set; }
    public decimal   Hours          { get; private set; }

    public decimal   RateCost       { get; private set; }  // snapshot, siempre presente
    public decimal?  RateBilled     { get; private set; }  // NULL si billable = false
    public bool      Billable       { get; private set; } = true;

    public string    Status         { get; private set; } = "pending";
    // 'pending' | 'approved' | 'billed'
    public string    Currency       { get; private set; } = "COP";

    public DateTime? BilledAt       { get; private set; }
    public Guid?     CreatedBy      { get; private set; }

    public DateTime? DeletedAt      { get; private set; }

    private TimeEntry() { }

    public static TimeEntry Create(
        Guid     organizationId,
        Guid     projectId,
        DateOnly date,
        decimal  hours,
        decimal  rateCost,
        string   currency       = "COP",
        bool     billable       = true,
        decimal? rateBilled     = null,
        string?  description    = null,
        Guid?    collaboratorId = null,
        Guid?    createdBy      = null)
    {
        return new TimeEntry
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            Date           = date,
            Hours          = hours,
            RateCost       = rateCost,
            Currency       = currency,
            Billable       = billable,
            RateBilled     = billable ? rateBilled : null,
            Description    = description,
            CollaboratorId = collaboratorId,
            CreatedBy      = createdBy
        };
    }

    public void Update(string? description, DateOnly date, decimal hours, decimal rateCost, decimal? rateBilled, bool billable)
    {
        Description = description;
        Date        = date;
        Hours       = hours;
        RateCost    = rateCost;
        RateBilled  = billable ? rateBilled : null;
        Billable    = billable;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void Approve()  { Status = "approved"; UpdatedAt = DateTime.UtcNow; }

    public void MarkBilled()
    {
        Status    = "billed";
        BilledAt  = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

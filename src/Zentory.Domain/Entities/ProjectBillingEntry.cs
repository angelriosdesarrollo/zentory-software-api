using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectBillingEntry : TenantEntity
{
    public Guid      ProjectId  { get; private set; }
    public string    Concept    { get; private set; } = default!;
    public string    Period     { get; private set; } = default!;
    public int?      Hours      { get; private set; }
    public decimal   Rate       { get; private set; }
    public decimal?  Amount     { get; private set; }
    public string    Status     { get; private set; } = "PENDING";
    // "PENDING" | "INVOICED" | "PAID"
    public string?   InvoiceRef { get; private set; }
    public DateOnly  DueDate    { get; private set; }

    private ProjectBillingEntry() { }

    public static ProjectBillingEntry Create(
        Guid     organizationId,
        Guid     projectId,
        string   concept,
        string   period,
        decimal  rate,
        DateOnly dueDate,
        int?     hours      = null,
        decimal? amount     = null,
        string   status     = "PENDING",
        string?  invoiceRef = null)
    {
        return new ProjectBillingEntry
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            Concept        = concept,
            Period         = period,
            Hours          = hours,
            Rate           = rate,
            Amount         = amount,
            Status         = status,
            InvoiceRef     = invoiceRef,
            DueDate        = dueDate,
        };
    }
}

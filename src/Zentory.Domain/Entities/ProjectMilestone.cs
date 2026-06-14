using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectMilestone : TenantEntity
{
    public Guid      ProjectId { get; private set; }
    public string    Name      { get; private set; } = default!;
    public string    Status    { get; private set; } = "PENDING";
    // "PENDING" | "IN_PROGRESS" | "DONE"
    public decimal   Value     { get; private set; }
    public DateOnly? DueDate   { get; private set; }

    private ProjectMilestone() { }

    public static ProjectMilestone Create(
        Guid     organizationId,
        Guid     projectId,
        string   name,
        decimal  value,
        DateOnly? dueDate = null,
        string   status  = "PENDING")
    {
        return new ProjectMilestone
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            Name           = name,
            Value          = value,
            DueDate        = dueDate,
            Status         = status,
        };
    }

    public void UpdateStatus(string status) { Status = status; UpdatedAt = DateTime.UtcNow; }
}

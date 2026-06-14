using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectDeliverable : TenantEntity
{
    public Guid      ProjectId   { get; private set; }
    public Guid?     MilestoneId { get; private set; }
    public string    Name        { get; private set; } = default!;
    public string    Type        { get; private set; } = default!;
    // "Documento" | "Diseño" | "Código" | "Video" | "Prototipo" | "Otro"
    public string    Status      { get; private set; } = "PENDING";
    // "PENDING" | "IN_REVIEW" | "APPROVED" | "REJECTED"
    public DateOnly? DueDate     { get; private set; }
    public string?   ApprovedBy  { get; private set; }

    private ProjectDeliverable() { }

    public static ProjectDeliverable Create(
        Guid     organizationId,
        Guid     projectId,
        string   name,
        string   type,
        DateOnly? dueDate     = null,
        Guid?    milestoneId  = null,
        string   status       = "PENDING",
        string?  approvedBy   = null)
    {
        return new ProjectDeliverable
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            MilestoneId    = milestoneId,
            Name           = name,
            Type           = type,
            DueDate        = dueDate,
            Status         = status,
            ApprovedBy     = approvedBy,
        };
    }

    public void UpdateStatus(string status, string? approvedBy = null)
    {
        Status     = status;
        ApprovedBy = approvedBy;
        UpdatedAt  = DateTime.UtcNow;
    }
}

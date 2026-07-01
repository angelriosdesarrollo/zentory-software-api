using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectTask : TenantEntity
{
    public Guid    ProjectId   { get; private set; }
    public string  Title       { get; private set; } = default!;
    public string  Status      { get; private set; } = "todo";
    // "todo" | "in_progress" | "review" | "done"
    public string  Priority    { get; private set; } = "medium";
    // "low" | "medium" | "high"
    public string? Description  { get; private set; }
    public Guid?   AssigneeId   { get; private set; }
    public DateOnly? DueDate    { get; private set; }
    public DateTime? DeletedAt  { get; private set; }

    // ── Gantt fields ──────────────────────────────────────────────────────────
    public Guid?     MilestoneId  { get; private set; }
    public DateOnly? StartDate    { get; private set; }
    public int       Hours        { get; private set; }
    public string[]  Dependencies { get; private set; } = [];

    private ProjectTask() { }

    public static ProjectTask Create(
        Guid     organizationId,
        Guid     projectId,
        string   title,
        string   status      = "todo",
        string   priority    = "medium",
        string?  description = null,
        Guid?    assigneeId  = null,
        DateOnly? dueDate    = null,
        Guid?    milestoneId = null,
        DateOnly? startDate  = null,
        int      hours       = 0,
        string[]? dependencies = null)
    {
        return new ProjectTask
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            Title          = title,
            Status         = status,
            Priority       = priority,
            Description    = description,
            AssigneeId     = assigneeId,
            DueDate        = dueDate,
            MilestoneId    = milestoneId,
            StartDate      = startDate,
            Hours          = hours,
            Dependencies   = dependencies ?? [],
        };
    }

    public void Move(string newStatus)
    {
        Status    = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string title, string priority, string? description, DateOnly? dueDate)
    {
        Title       = title;
        Priority    = priority;
        Description = description;
        DueDate     = dueDate;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void UpdateGantt(Guid? milestoneId, DateOnly? startDate, DateOnly? dueDate, int hours, string[] dependencies)
    {
        MilestoneId  = milestoneId;
        StartDate    = startDate;
        DueDate      = dueDate;
        Hours        = hours;
        Dependencies = dependencies;
        UpdatedAt    = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

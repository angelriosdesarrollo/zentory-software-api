using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectActivityLog : TenantEntity
{
    public Guid   ProjectId    { get; private set; }
    public string UserInitials { get; private set; } = default!;
    public string Action       { get; private set; } = default!;
    public string Module       { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }

    private ProjectActivityLog() { }

    public static ProjectActivityLog Create(
        Guid     organizationId,
        Guid     projectId,
        string   userInitials,
        string   action,
        string   module,
        DateTime? occurredAt = null)
    {
        return new ProjectActivityLog
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            UserInitials   = userInitials,
            Action         = action,
            Module         = module,
            OccurredAt     = occurredAt ?? DateTime.UtcNow,
        };
    }
}

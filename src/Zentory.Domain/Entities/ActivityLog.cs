using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ActivityLog : TenantEntity
{
    public string   EntityType   { get; private set; } = default!;  // "Project" | "Proposal" | ...
    public Guid     EntityId     { get; private set; }
    public string?  EntityCode   { get; private set; }  // e.g. "ZEN-007", "#P-001"
    public Guid?    UserId       { get; private set; }
    public string   UserInitials { get; private set; } = default!;
    public string   Action       { get; private set; } = default!;  // mensaje en español
    public string?  Metadata     { get; private set; }  // JSONB opcional
    public DateTime OccurredAt   { get; private set; }

    private ActivityLog() { }

    public static ActivityLog Create(
        Guid    organizationId,
        string  entityType,
        Guid    entityId,
        string  userInitials,
        string  action,
        string? entityCode   = null,
        string? metadata     = null,
        Guid?   userId       = null,
        DateTime? occurredAt = null)
    {
        return new ActivityLog
        {
            OrganizationId = organizationId,
            EntityType     = entityType,
            EntityId       = entityId,
            EntityCode     = entityCode,
            UserId         = userId,
            UserInitials   = userInitials,
            Action         = action,
            Metadata       = metadata,
            OccurredAt     = occurredAt ?? DateTime.UtcNow,
        };
    }
}

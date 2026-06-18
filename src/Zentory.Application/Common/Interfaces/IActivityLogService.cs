namespace Zentory.Application.Common.Interfaces;

public interface IActivityLogService
{
    Task LogAsync(
        string    entityType,
        Guid      entityId,
        string    action,
        string?   entityCode = null,
        string?   metadata   = null,
        CancellationToken ct = default);

    // For unauthenticated/public events where tenant context is unavailable.
    Task LogPublicAsync(
        Guid      organizationId,
        string    entityType,
        Guid      entityId,
        string    userInitials,
        string    action,
        string?   entityCode = null,
        string?   metadata   = null,
        CancellationToken ct = default);
}

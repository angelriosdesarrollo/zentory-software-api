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
}

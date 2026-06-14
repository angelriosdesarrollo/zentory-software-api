namespace Zentory.Application.ActivityLogs;

public record ActivityLogDto(
    Guid   Id,
    string UserInitials,
    string Action,
    string EntityCode,
    string OccurredAt);

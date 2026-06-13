namespace Zentory.Application.TimeEntries.DTOs;

public record TimeEntryDto(
    Guid      Id,
    Guid      ProjectId,
    string    ProjectName,
    Guid?     CollaboratorId,
    string?   CollaboratorName,
    string?   Description,
    DateOnly  Date,
    decimal   Hours,
    decimal   RateCost,
    decimal?  RateBilled,
    bool      Billable,
    string    Status,
    string    Currency,
    DateTime? BilledAt,
    DateTime  CreatedAt,
    DateTime  UpdatedAt);

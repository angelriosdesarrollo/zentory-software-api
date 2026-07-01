namespace Zentory.Application.ProjectTasks.DTOs;

public record ProjectTaskDto(
    Guid      Id,
    Guid      ProjectId,
    string    Title,
    string    Status,
    string    Priority,
    string?   Description,
    Guid?     AssigneeId,
    string?   AssigneeName,
    string?   DueDate,
    DateTime  CreatedAt,
    DateTime  UpdatedAt,
    // Gantt fields
    Guid?     MilestoneId,
    string?   StartDate,
    int       Hours,
    string[]  Dependencies);

namespace Zentory.Application.ProjectShares.DTOs;

public record ProjectShareDto(
    Guid      Id,
    string    Token,
    Guid      ProjectId,
    Guid      CreatedBy,
    DateTime  CreatedAt,
    DateTime? ExpiresAt,
    string?   Message,
    string[]  IncludedFileIds,
    string[]  IncludedDeliverableIds);

// ── Public view DTOs (no auth) ────────────────────────────────────────────────

public record ProjectShareMilestoneDto(
    string  Id,
    string  Name,
    string  Status,
    string  DueDate,
    decimal Value);

public record ProjectShareDocumentDto(
    string  Id,
    string  Name,
    string  Kind,       // "file" | "link"
    string? MimeType,
    string? Size,
    string? Url,
    string  Source);    // "file" | "deliverable"

public record ProjectSharePublicDto(
    string  ProjectName,
    string  ClientName,
    int     Progress,
    string  HealthStatus,
    string? StartDate,
    string? EndDate,
    string? Message,
    IReadOnlyList<ProjectShareMilestoneDto>  Milestones,
    IReadOnlyList<ProjectShareDocumentDto>   Documents);

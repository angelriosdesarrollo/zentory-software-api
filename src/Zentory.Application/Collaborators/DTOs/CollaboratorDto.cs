namespace Zentory.Application.Collaborators.DTOs;

public record CollaboratorSummaryDto(
    Guid     Id,
    string   Name,
    string   Type,
    string   Status,
    string?  Role,
    string?  Email,
    decimal? HourlyRate,
    decimal? MonthlyRate,
    string   Currency,
    string   PilaStatus);

public record CollaboratorDto(
    Guid     Id,
    string   Name,
    string   Type,
    string   Status,
    string?  Role,
    string?  Email,
    string?  Phone,
    string?  IdNumber,
    decimal? HourlyRate,
    decimal? MonthlyRate,
    string   Currency,
    string   PilaStatus,
    string?  PilaLastVerifiedPeriod,
    short?   ArlRiskLevel,
    Guid?    UserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

namespace Zentory.Application.Projects.DTOs;

public record ProjectSummaryDto(
    Guid      Id,
    string    Name,
    Guid      ClientId,
    string    ClientName,
    string    Status,
    string    BillingType,
    decimal   ContractValue,
    string    Currency,
    int       HoursTotal,
    int       HoursUsed,
    DateTime? StartDate,
    DateTime? EndDate);

public record ProjectDto(
    Guid      Id,
    string    Name,
    Guid      ClientId,
    string    ClientName,
    string    Status,
    string    BillingType,
    decimal   ContractValue,
    string    Currency,
    int       HoursTotal,
    int       HoursUsed,
    DateTime? StartDate,
    DateTime? EndDate,
    Guid?     ProposalId,
    DateTime  CreatedAt,
    DateTime  UpdatedAt);

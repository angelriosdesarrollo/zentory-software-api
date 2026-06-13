namespace Zentory.Application.Master.DTOs;

public record SsRuleDto(
    Guid    Id,
    string  FundType,
    string  ContributorType,
    decimal EmployeePct,
    decimal EmployerPct,
    decimal TotalPct,
    decimal MinBaseSmlv,
    decimal? MaxBaseSmlv,
    decimal SmlvCop,
    short?  ArlLevel,
    string? Notes);

public record ExpenseCategoryDto(
    short  Id,
    string Name,
    string Slug,
    string Type);

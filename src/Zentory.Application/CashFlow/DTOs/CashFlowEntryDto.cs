namespace Zentory.Application.CashFlow.DTOs;

public record CashFlowEntryDto(
    string  Id,
    string  Date,           // "YYYY-MM-DD"
    string  Concept,
    string  Type,           // "income" | "expense"
    string? CategorySlug,
    string? CategoryName,
    string? ProjectId,
    decimal Amount,
    string  Currency,
    string  Status          // "confirmed" | "pending"
);

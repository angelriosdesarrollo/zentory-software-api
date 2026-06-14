namespace Zentory.Application.Organization.DTOs;

public record OrgPlanDto(
    string   Plan,
    string   AccountType,
    string?  RenewsAt,
    bool     CancelAtPeriodEnd,
    decimal? PriceMonthlyUsd,
    string   Currency);

public record BillingHistoryItemDto(
    string   PaidAt,
    decimal  Amount,
    string   Currency,
    string?  InvoiceNumber,
    string   Status);

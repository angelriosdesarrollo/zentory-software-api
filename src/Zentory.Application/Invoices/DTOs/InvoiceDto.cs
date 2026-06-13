namespace Zentory.Application.Invoices.DTOs;

public record InvoiceItemDto(
    Guid    Id,
    string  Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct,
    decimal Total,
    short   SortOrder);

public record InvoiceSummaryDto(
    Guid     Id,
    string   InvoiceNumber,
    Guid     ClientId,
    string   ClientName,
    Guid?    ProjectId,
    string   Status,
    string   DocumentType,
    DateOnly IssuedAt,
    DateOnly DueAt,
    decimal  Total,
    decimal  AmountPaid,
    decimal  AmountDue,
    string   Currency);

public record InvoiceDto(
    Guid                    Id,
    string                  InvoiceNumber,
    Guid                    ClientId,
    string                  ClientName,
    Guid?                   ProjectId,
    string                  Status,
    string                  DocumentType,
    DateOnly                IssuedAt,
    DateOnly                DueAt,
    decimal                 Subtotal,
    decimal                 TaxAmount,
    decimal                 Total,
    decimal                 AmountPaid,
    decimal                 AmountDue,
    string                  Currency,
    string?                 Notes,
    string?                 PaymentTerms,
    string?                 PaymentInstructions,
    IReadOnlyList<InvoiceItemDto> Items,
    DateTime                CreatedAt,
    DateTime                UpdatedAt);

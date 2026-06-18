namespace Zentory.Application.Proposals.DTOs;

public record ProposalItemDto(
    Guid    Id,
    string  Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct,
    decimal Total,
    short   SortOrder);

public record ProposalSectionDto(
    Guid     Id,
    string   SectionType,
    string?  Title,
    string?  Content,
    short    SortOrder,
    bool     IsVisible,
    bool     IsEncrypted,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record ProposalSummaryDto(
    Guid      Id,
    string    Title,
    Guid      ClientId,
    string    ClientName,
    string    Status,
    decimal?  TotalAmount,
    string    Currency,
    DateTime? ExpiresAt,
    DateTime? SentAt,
    int       ViewCount,
    DateTime  CreatedAt);

public record ProposalDto(
    Guid                           Id,
    Guid                           PublicToken,
    string                         Title,
    Guid                           ClientId,
    string                         ClientName,
    string                         Status,
    decimal?                       TotalAmount,
    string                         Currency,
    string?                        IntroText,
    DateTime?                      ExpiresAt,
    DateTime?                      SentAt,
    DateTime?                      AcceptedAt,
    DateTime?                      RejectedAt,
    int                            ViewCount,
    Guid?                          ConvertedToProjectId,
    IReadOnlyList<ProposalItemDto>    Items,
    IReadOnlyList<ProposalSectionDto> Sections,
    DateTime                       CreatedAt,
    DateTime                       UpdatedAt);

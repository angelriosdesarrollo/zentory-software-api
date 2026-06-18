namespace Zentory.Application.Proposals.DTOs;

public record PublicSectionDto(
    string  SectionType,
    string? Title,
    string? Content,
    short   SortOrder,
    bool    IsVisible);

public record PublicItemDto(
    string  Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPct,
    decimal Total,
    short   SortOrder);

public record PublicProposalDto(
    Guid      Id,
    string    Title,
    string    Status,
    string    ClientName,
    string    OrganizationName,
    string?   OrganizationLogoUrl,
    string    Currency,
    decimal?  TotalAmount,
    DateTime? SentAt,
    DateTime? ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? RejectedAt,
    int       ViewCount,
    DateTime? LastViewedAt,
    IReadOnlyList<PublicSectionDto> Sections,
    IReadOnlyList<PublicItemDto>    Items);

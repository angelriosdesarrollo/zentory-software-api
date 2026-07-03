namespace Zentory.Application.Plans.DTOs;

public record PlansPageDto(
    PlansByLegalTypeDto Freelance,
    PlansByLegalTypeDto Empresa);

public record PlansByLegalTypeDto(
    IReadOnlyList<PlanDataDto> Plans,
    IReadOnlyList<CompareRowDto> CompareRows);

public record PlanDataDto(
    string   Id,
    string   Name,
    decimal  PriceMonthlyUsd,
    decimal  PriceAnnualUsd,
    string   Tagline,
    string   CtaText,
    bool     IsPopular,
    string?  FeaturesHeading,
    IReadOnlyList<PlanFeatureItemDto> Features,
    IReadOnlyList<PlanLimitDto> Limits);

public record PlanFeatureItemDto(
    string  Text,
    bool    IsHighlight,
    string? BadgeText);

public record PlanLimitDto(
    string FeatureKey,
    int?   LimitValue);

public record CompareRowDto(
    string  FeatureName,
    bool    IsEmpresaOnly,
    string? FreeValue,
    string? ProValue,
    string? StudioValue);

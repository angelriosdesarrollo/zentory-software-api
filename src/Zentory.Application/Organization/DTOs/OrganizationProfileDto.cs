namespace Zentory.Application.Organization.DTOs;

public record OrganizationProfileDto(
    Guid    Id,
    string  Name,
    string  Plan,
    string  AccountType,
    string  Country,
    string? LegalName,
    string? LogoUrl,
    string? Nit,
    string? CompanyType,
    string? LegalRep,
    string? LegalRepId,
    string? TaxRegime,
    string? Ciiu,
    string? Email,
    string? Phone,
    string? Address,
    string? City);

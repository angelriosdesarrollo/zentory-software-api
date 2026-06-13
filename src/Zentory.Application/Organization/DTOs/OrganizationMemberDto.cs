namespace Zentory.Application.Organization.DTOs;

public record OrganizationMemberDto(
    Guid   UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    DateTime JoinedAt);

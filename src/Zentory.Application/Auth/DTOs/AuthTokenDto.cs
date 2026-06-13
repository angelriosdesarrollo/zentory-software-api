namespace Zentory.Application.Auth.DTOs;

public record AuthTokenDto(
    string         AccessToken,
    string         RefreshToken,
    int            ExpiresIn,
    UserProfileDto User);

public record UserProfileDto(
    Guid   Id,
    string FirstName,
    string LastName,
    string Email,
    string Plan,
    string AccountType,
    string Role,
    string OrganizationName);

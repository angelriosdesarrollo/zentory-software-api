namespace Zentory.Application.Auth.DTOs;

public record AuthTokenDto(
    string                          AccessToken,
    string                          RefreshToken,
    int                             ExpiresIn,
    UserProfileDto                  User,
    IReadOnlyList<OrgMembershipDto> Memberships);

public record SwitchOrgResponseDto(
    string                          AccessToken,
    int                             ExpiresIn,
    UserProfileDto                  User,
    IReadOnlyList<OrgMembershipDto> Memberships);

public record UserProfileDto(
    Guid   Id,
    string FirstName,
    string LastName,
    string Email,
    string Plan,
    string AccountType,
    string Role,
    string ActiveOrgId,
    string ActiveOrgName,
    string ActiveOrgRole);

public record OrgMembershipDto(
    string OrgId,
    string OrgName,
    string AccountType,
    string Plan,
    string Role,
    string JoinedAt);

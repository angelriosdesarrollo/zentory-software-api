namespace Zentory.Application.Auth.DTOs;

public record AuthTokenDto(
    string                          AccessToken,
    string                          RefreshToken,
    int                             ExpiresIn,
    UserProfileDto                  User,
    IReadOnlyList<OrgMembershipDto> Memberships,
    bool                            IsNewUser = false);

public record SwitchOrgResponseDto(
    string                          AccessToken,
    int                             ExpiresIn,
    UserProfileDto                  User,
    IReadOnlyList<OrgMembershipDto> Memberships);

public record UserProfileDto(
    Guid      Id,
    string    FirstName,
    string    LastName,
    string    Email,
    string    Plan,
    string    LegalType,
    string    Role,
    string    ActiveOrgId,
    string    ActiveOrgName,
    string    ActiveOrgRole,
    DateTime? TermsAcceptedAt      = null,
    string?   TermsAcceptedVersion = null);

public record OrgMembershipDto(
    string OrgId,
    string OrgName,
    string LegalType,
    string Plan,
    string Role,
    string JoinedAt);

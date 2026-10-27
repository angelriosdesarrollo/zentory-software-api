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

// Plan, LegalType y ActiveOrg* son de la organización activa: null en la sesión sin organización (PLAN-0018 RF-85, D-44).
public record UserProfileDto(
    Guid      Id,
    string    FirstName,
    string    LastName,
    string    Email,
    string?   Plan,
    string?   LegalType,
    string    Role,
    string?   ActiveOrgId,
    string?   ActiveOrgName,
    string?   ActiveOrgRole,
    DateTime? TermsAcceptedAt      = null,
    string?   TermsAcceptedVersion = null,
    // Métodos de acceso de la cuenta (PLAN-0018 RF-81 h): la Zona de peligro elige entre contraseña y Google.
    bool      HasPassword          = false,
    bool      HasGoogle            = false,
    // Verificación del email (PLAN-0018 RF-20): el frontend muestra el aviso mientras sea false.
    bool      EmailVerified        = false);

// Plan es el efectivo del owner de la organización, no el del usuario (PLAN-0018 RF-76 d, C-11).
public record OrgMembershipDto(
    string  OrgId,
    string  OrgName,
    string  LegalType,
    string  Plan,
    string  Role,
    string  JoinedAt,
    string? OwnerName = null);

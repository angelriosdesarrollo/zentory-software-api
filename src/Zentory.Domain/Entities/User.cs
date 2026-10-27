using Zentory.Domain.Common;

namespace Zentory.Domain.Entities;

public class User
{
    public Guid      UserId         { get; private set; } = Guid.NewGuid();
    public Guid?     OrganizationId { get; private set; }
    public string    Email          { get; private set; } = default!;
    public string?   PasswordHash   { get; private set; }
    public string?   GoogleId       { get; private set; }
    public string    FirstName      { get; private set; } = default!;
    public string    LastName       { get; private set; } = default!;
    public string    Role           { get; private set; } = default!;
    public bool      IsActive       { get; private set; } = true;
    public DateTime  CreatedAt      { get; private set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt      { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// When the user accepted the Terms and Privacy Policy checkbox at registration.
    /// Null means never accepted (should not happen post-TASK-BE-035, but kept nullable
    /// since it maps directly to a nullable DB column and pre-existing seeded users have none).
    /// This is the evidence Habeas Data (Ley 1581) requires being able to produce on request.
    /// </summary>
    public DateTime? TermsAcceptedAt      { get; private set; }
    public string?   TermsAcceptedVersion { get; private set; }

    // Última organización activa elegida con POST /organization/switch (PLAN-0018 RF-70, D-27). Sin FK a propósito: la
    // purga de organizaciones la pone en null y el login la ignora si la membresía ya no está vigente.
    public Guid?     LastActiveOrganizationId { get; private set; }

    // Token de concurrencia de max_owned_orgs (PLAN-0018 RF-74 a, SR-0018.35; D-45): crear, restaurar o recibir una
    // organización llaman a TouchOwnership() en la misma escritura, así dos operaciones concurrentes del mismo usuario no
    // superan el límite. Las demás escrituras de User no lo regeneran.
    public Guid      OwnershipStamp           { get; private set; } = Guid.NewGuid();

    // Verificación del email (PLAN-0018 RF-20, M1): null hasta verificar. Las personas demo quedan verificadas al
    // sembrarse; Google e invitaciones la marcan al crear la cuenta cuando llegan sus tareas.
    public DateTime? EmailVerifiedAt          { get; private set; }

    public bool      IsEmailVerified          => EmailVerifiedAt is not null;

    // Sesión vigente del usuario de la app (PLAN-0018 RF-90 a, D-47): el sid del último inicio de sesión. Null hasta el
    // primer login desde M2c, y entonces ningún sid es el vigente. Solo StartSession() la escribe; no es token de
    // concurrencia (de dos logins simultáneos gana el último que confirma).
    public Guid?     CurrentSessionId         { get; private set; }

    private User() { }

    public static User Create(
        string    email,
        string    firstName,
        string    lastName,
        string    role,
        string?   passwordHash         = null,
        Guid?     id                   = null,
        Guid?     organizationId       = null,
        DateTime? termsAcceptedAt      = null,
        string?   termsAcceptedVersion = null,
        string?   googleId             = null)
    {
        return new User
        {
            UserId               = id ?? Guid.NewGuid(),
            OrganizationId       = organizationId,
            Email                = EmailAddress.Normalize(email),
            FirstName            = firstName,
            LastName             = lastName,
            Role                 = role,
            PasswordHash         = passwordHash,
            TermsAcceptedAt      = termsAcceptedAt,
            TermsAcceptedVersion = termsAcceptedVersion,
            GoogleId             = googleId
        };
    }

    public void SetLastActiveOrganization(Guid organizationId)
    {
        LastActiveOrganizationId = organizationId;
        UpdatedAt                = DateTime.UtcNow;
    }

    // Inicia una sesión nueva y la deja como la vigente (RF-90 b): un Guid aleatorio, sin derivar del usuario, del
    // dispositivo ni de la hora (RNF-10). Lo llaman solo login, Google, registro y la aceptación de invitación sin JWT.
    public Guid StartSession()
    {
        var sessionId    = Guid.NewGuid();
        CurrentSessionId = sessionId;
        UpdatedAt        = DateTime.UtcNow;
        return sessionId;
    }

    public void TouchOwnership()
    {
        OwnershipStamp = Guid.NewGuid();
        UpdatedAt      = DateTime.UtcNow;
    }

    public void LinkGoogleAccount(string googleId)
    {
        GoogleId  = googleId;
        UpdatedAt = DateTime.UtcNow;
    }

    // Idempotente: cuenta la primera verificación.
    public void MarkEmailVerified()
    {
        if (EmailVerifiedAt is not null) return;
        var now         = DateTime.UtcNow;
        EmailVerifiedAt = now;
        UpdatedAt       = now;
    }

    // La cuenta queda sin contraseña (solo Google): el login por contraseña responde el error genérico de credenciales.
    public void ClearPassword()
    {
        PasswordHash = null;
        UpdatedAt    = DateTime.UtcNow;
    }

    // Recibe el hash ya calculado: el dominio no conoce el algoritmo ni la política de contraseñas.
    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        UpdatedAt    = DateTime.UtcNow;
    }
}

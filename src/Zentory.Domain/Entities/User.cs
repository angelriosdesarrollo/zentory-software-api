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
            Email                = email,
            FirstName            = firstName,
            LastName             = lastName,
            Role                 = role,
            PasswordHash         = passwordHash,
            TermsAcceptedAt      = termsAcceptedAt,
            TermsAcceptedVersion = termsAcceptedVersion,
            GoogleId             = googleId
        };
    }

    public void LinkGoogleAccount(string googleId)
    {
        GoogleId  = googleId;
        UpdatedAt = DateTime.UtcNow;
    }
}

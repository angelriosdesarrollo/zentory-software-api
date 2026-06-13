namespace Zentory.Domain.Entities;

public class User
{
    public Guid      UserId         { get; private set; } = Guid.NewGuid();
    public Guid      OrganizationId { get; private set; }
    public string    Email          { get; private set; } = default!;
    public string?   PasswordHash   { get; private set; }
    public string    FirstName      { get; private set; } = default!;
    public string    LastName       { get; private set; } = default!;
    public string    Role           { get; private set; } = default!;
    public bool      IsActive       { get; private set; } = true;
    public DateTime  CreatedAt      { get; private set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt      { get; private set; } = DateTime.UtcNow;

    private User() { }

    public static User Create(
        Guid    organizationId,
        string  email,
        string  firstName,
        string  lastName,
        string  role,
        string? passwordHash = null)
    {
        return new User
        {
            OrganizationId = organizationId,
            Email          = email,
            FirstName      = firstName,
            LastName       = lastName,
            Role           = role,
            PasswordHash   = passwordHash
        };
    }
}

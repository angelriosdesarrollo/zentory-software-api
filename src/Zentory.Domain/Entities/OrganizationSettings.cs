namespace Zentory.Domain.Entities;

// Composite PK: (OrganizationId, Key). No inheritance needed.
public class OrganizationSettings
{
    public Guid     OrganizationId { get; private set; }
    public string   Key            { get; private set; } = default!;
    public string?  Value          { get; private set; }
    public DateTime CreatedAt      { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt      { get; private set; } = DateTime.UtcNow;

    private OrganizationSettings() { }

    public static OrganizationSettings Set(Guid organizationId, string key, string? value)
    {
        return new OrganizationSettings
        {
            OrganizationId = organizationId,
            Key            = key,
            Value          = value
        };
    }

    public void Update(string? value) { Value = value; UpdatedAt = DateTime.UtcNow; }
}

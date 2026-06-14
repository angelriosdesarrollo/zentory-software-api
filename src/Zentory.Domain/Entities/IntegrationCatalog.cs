namespace Zentory.Domain.Entities;

public class IntegrationCatalog
{
    public string   Id          { get; private set; } = default!;
    public string   Name        { get; private set; } = default!;
    public string   Description { get; private set; } = default!;
    public bool     IsEnabled   { get; private set; } = true;
    public bool     IsHidden    { get; private set; } = false;
    public int      SortOrder   { get; private set; }
    public DateTime CreatedAt   { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt   { get; private set; } = DateTime.UtcNow;

    public ICollection<OrganizationIntegration> OrganizationIntegrations { get; private set; } = [];

    private IntegrationCatalog() { }

    public static IntegrationCatalog Create(string id, string name, string description, int sortOrder = 0) =>
        new() { Id = id, Name = name, Description = description, SortOrder = sortOrder };

    public void SetHidden(bool hidden)
    {
        IsHidden  = hidden;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        UpdatedAt = DateTime.UtcNow;
    }
}

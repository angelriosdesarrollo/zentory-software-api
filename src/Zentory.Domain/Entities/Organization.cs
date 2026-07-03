namespace Zentory.Domain.Entities;

public class Organization
{
    public Guid   OrganizationId { get; private set; } = Guid.NewGuid();
    public Guid?  OwnerId        { get; private set; }
    public string Name           { get; private set; } = default!;
    public string LegalType    { get; private set; } = default!;
    public string Country        { get; private set; } = "CO";
    public bool   IsActive       { get; private set; } = true;

    private Organization() { }

    public static Organization Create(string name, string legalType, string country = "CO", Guid? id = null)
    {
        return new Organization
        {
            OrganizationId = id ?? Guid.NewGuid(),
            Name           = name,
            LegalType    = legalType,
            Country        = country
        };
    }

    public void SetOwner(Guid userId)      { OwnerId = userId; }
}

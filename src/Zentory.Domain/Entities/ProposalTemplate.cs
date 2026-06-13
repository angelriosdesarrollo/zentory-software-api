using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProposalTemplate : TenantEntity
{
    public string   Name        { get; private set; } = default!;
    public string?  Description { get; private set; }
    public string   Structure   { get; private set; } = "[]";  // JSONB: array de secciones
    public bool     IsDefault   { get; private set; }
    public DateTime? DeletedAt  { get; private set; }

    private ProposalTemplate() { }

    public static ProposalTemplate Create(
        Guid    organizationId,
        string  name,
        string? description = null,
        string  structure   = "[]",
        bool    isDefault   = false)
    {
        return new ProposalTemplate
        {
            OrganizationId = organizationId,
            Name           = name,
            Description    = description,
            Structure      = structure,
            IsDefault      = isDefault
        };
    }

    public void Update(string name, string? description, string structure, bool isDefault)
    {
        Name        = name;
        Description = description;
        Structure   = structure;
        IsDefault   = isDefault;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

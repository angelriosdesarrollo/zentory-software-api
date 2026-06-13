using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ServiceCatalog : TenantEntity
{
    public string   Name         { get; private set; } = default!;
    public string?  Description  { get; private set; }
    public short?   UnitId       { get; private set; }
    public decimal? DefaultPrice { get; private set; }
    public string   Currency     { get; private set; } = "COP";
    public string?  Category     { get; private set; }
    public bool     IsActive     { get; private set; } = true;
    public DateTime? DeletedAt   { get; private set; }

    private ServiceCatalog() { }

    public static ServiceCatalog Create(
        Guid    organizationId,
        string  name,
        string? description  = null,
        short?  unitId       = null,
        decimal? defaultPrice= null,
        string  currency     = "COP",
        string? category     = null)
    {
        return new ServiceCatalog
        {
            OrganizationId = organizationId,
            Name           = name,
            Description    = description,
            UnitId         = unitId,
            DefaultPrice   = defaultPrice,
            Currency       = currency,
            Category       = category
        };
    }

    public void Update(string name, string? description, decimal? defaultPrice, string? category, bool isActive)
    {
        Name         = name;
        Description  = description;
        DefaultPrice = defaultPrice;
        Category     = category;
        IsActive     = isActive;
        UpdatedAt    = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

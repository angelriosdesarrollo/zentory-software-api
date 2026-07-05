using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class Client : TenantEntity
{
    public string Name        { get; private set; } = default!;
    public string ContactName { get; private set; } = default!;
    public string? Email      { get; private set; }
    public string? Phone      { get; private set; }
    public string? City       { get; private set; }
    public string? Address    { get; private set; }
    public string? Nit        { get; private set; }
    public string? Notes      { get; private set; }
    public bool IsDeleted     { get; private set; }

    private Client() { }

    public static Client Create(
        Guid organizationId,
        string name,
        string contactName,
        string? email   = null,
        string? phone   = null,
        string? city    = null,
        string? address = null,
        string? nit     = null,
        string? notes   = null)
    {
        return new Client
        {
            OrganizationId = organizationId,
            Name           = name,
            ContactName    = contactName,
            Email          = email,
            Phone          = phone,
            City           = city,
            Address        = address,
            Nit            = nit,
            Notes          = notes
        };
    }

    public void Update(
        string name,
        string contactName,
        string? email,
        string? phone,
        string? city,
        string? address,
        string? nit,
        string? notes)
    {
        Name        = name;
        ContactName = contactName;
        Email       = email;
        Phone       = phone;
        City        = city;
        Address     = address;
        Nit         = nit;
        Notes       = notes;
        Touch();
    }

    public void SoftDelete() => IsDeleted = true;
}

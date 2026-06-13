using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ClientContact : BaseEntity
{
    public Guid     OrganizationId { get; private set; }
    public Guid     ClientId       { get; private set; }
    public string   Name           { get; private set; } = default!;
    public string?  Email          { get; private set; }
    public string?  Phone          { get; private set; }
    public string?  Role           { get; private set; }  // 'CEO' | 'CTO' | 'Contabilidad'
    public bool     IsPrimary      { get; private set; }
    public DateTime? DeletedAt     { get; private set; }

    private ClientContact() { }

    public static ClientContact Create(
        Guid    organizationId,
        Guid    clientId,
        string  name,
        string? email    = null,
        string? phone    = null,
        string? role     = null,
        bool    isPrimary= false)
    {
        return new ClientContact
        {
            OrganizationId = organizationId,
            ClientId       = clientId,
            Name           = name,
            Email          = email,
            Phone          = phone,
            Role           = role,
            IsPrimary      = isPrimary
        };
    }

    public void Update(string name, string? email, string? phone, string? role)
    {
        Name      = name;
        Email     = email;
        Phone     = phone;
        Role      = role;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPrimary(bool value) { IsPrimary = value; UpdatedAt = DateTime.UtcNow; }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

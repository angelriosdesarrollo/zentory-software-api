using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class OrganizationMember : BaseEntity
{
    public Guid     OrganizationId { get; private set; }
    public Guid     UserId         { get; private set; }
    public string   Role           { get; private set; } = "member";
    // 'owner' | 'admin' | 'member'
    public Guid?    InvitedBy      { get; private set; }
    public DateTime JoinedAt       { get; private set; } = DateTime.UtcNow;
    public DateTime? DeletedAt     { get; private set; }

    private OrganizationMember() { }

    public static OrganizationMember Create(
        Guid    organizationId,
        Guid    userId,
        string  role      = "member",
        Guid?   invitedBy = null)
    {
        return new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId         = userId,
            Role           = role,
            InvitedBy      = invitedBy
        };
    }

    public void ChangeRole(string role)   { Role = role; }
    public void SoftDelete()              { DeletedAt = DateTime.UtcNow; }
}

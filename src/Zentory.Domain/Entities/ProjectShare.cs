using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectShare : TenantEntity
{
    public Guid      ProjectId               { get; private set; }
    public string    Token                   { get; private set; } = default!;
    public Guid      CreatedBy               { get; private set; }
    public string?   Message                 { get; private set; }
    public DateTime? ExpiresAt               { get; private set; }
    public string[]  IncludedFileIds         { get; private set; } = [];
    public string[]  IncludedDeliverableIds  { get; private set; } = [];
    public DateTime? DeletedAt               { get; private set; }

    private ProjectShare() { }

    public static ProjectShare Create(
        Guid     organizationId,
        Guid     projectId,
        Guid     createdBy,
        string   token,
        string?  message                = null,
        DateTime? expiresAt             = null,
        string[]? includedFileIds       = null,
        string[]? includedDeliverableIds = null)
    {
        return new ProjectShare
        {
            OrganizationId          = organizationId,
            ProjectId               = projectId,
            CreatedBy               = createdBy,
            Token                   = token,
            Message                 = message,
            ExpiresAt               = expiresAt,
            IncludedFileIds         = includedFileIds ?? [],
            IncludedDeliverableIds  = includedDeliverableIds ?? [],
        };
    }

    public void Revoke() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

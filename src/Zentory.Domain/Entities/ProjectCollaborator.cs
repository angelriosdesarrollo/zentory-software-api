using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProjectCollaborator : BaseEntity
{
    public Guid     OrganizationId  { get; private set; }
    public Guid     ProjectId       { get; private set; }
    public Guid     CollaboratorId  { get; private set; }
    public string?  Role            { get; private set; }
    public decimal  RateCost        { get; private set; }   // snapshot al asignar
    public decimal? RateBilled      { get; private set; }   // snapshot al asignar
    public string   Currency        { get; private set; } = default!;
    public DateTime AssignedAt      { get; private set; } = DateTime.UtcNow;
    public DateTime? RemovedAt      { get; private set; }

    private ProjectCollaborator() { }

    public static ProjectCollaborator Assign(
        Guid    organizationId,
        Guid    projectId,
        Guid    collaboratorId,
        decimal rateCost,
        string  currency,
        decimal? rateBilled = null,
        string? role        = null)
    {
        return new ProjectCollaborator
        {
            OrganizationId = organizationId,
            ProjectId      = projectId,
            CollaboratorId = collaboratorId,
            RateCost       = rateCost,
            RateBilled     = rateBilled,
            Currency       = currency,
            Role           = role
        };
    }

    public void Remove() { RemovedAt = DateTime.UtcNow; }
}

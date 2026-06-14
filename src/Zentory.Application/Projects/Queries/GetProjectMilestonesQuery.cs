using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Projects.Queries;

public record ProjectMilestoneDto(
    Guid      Id,
    string    Name,
    string    Status,
    decimal   Value,
    string?   DueDate);

public record GetProjectMilestonesQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectMilestoneDto>>;

public sealed class GetProjectMilestonesQueryHandler
    : IRequestHandler<GetProjectMilestonesQuery, IReadOnlyList<ProjectMilestoneDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetProjectMilestonesQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProjectMilestoneDto>> Handle(
        GetProjectMilestonesQuery request,
        CancellationToken         cancellationToken)
    {
        return await _db.ProjectMilestones
            .Where(m => m.ProjectId == request.ProjectId && m.OrganizationId == _tenant.OrganizationId)
            .OrderBy(m => m.DueDate)
            .Select(m => new ProjectMilestoneDto(
                m.Id,
                m.Name,
                m.Status,
                m.Value,
                m.DueDate.HasValue ? m.DueDate.Value.ToString("yyyy-MM-dd") : null))
            .ToListAsync(cancellationToken);
    }
}

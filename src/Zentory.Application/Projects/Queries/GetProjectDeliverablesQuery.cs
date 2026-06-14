using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Projects.Queries;

public record ProjectDeliverableDto(
    Guid    Id,
    string  Name,
    string  Type,
    Guid?   MilestoneId,
    string  Status,
    string? DueDate,
    string? ApprovedBy);

public record GetProjectDeliverablesQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectDeliverableDto>>;

public sealed class GetProjectDeliverablesQueryHandler
    : IRequestHandler<GetProjectDeliverablesQuery, IReadOnlyList<ProjectDeliverableDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetProjectDeliverablesQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProjectDeliverableDto>> Handle(
        GetProjectDeliverablesQuery request,
        CancellationToken           cancellationToken)
    {
        return await _db.ProjectDeliverables
            .Where(d => d.ProjectId == request.ProjectId && d.OrganizationId == _tenant.OrganizationId)
            .OrderBy(d => d.DueDate)
            .Select(d => new ProjectDeliverableDto(
                d.Id,
                d.Name,
                d.Type,
                d.MilestoneId,
                d.Status,
                d.DueDate.HasValue ? d.DueDate.Value.ToString("yyyy-MM-dd") : null,
                d.ApprovedBy))
            .ToListAsync(cancellationToken);
    }
}

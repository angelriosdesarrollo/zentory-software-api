using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Projects.Queries;

public record ProjectActivityLogDto(
    Guid   Id,
    string UserInitials,
    string Action,
    string Module,
    string OccurredAt);

public record GetProjectActivityLogQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectActivityLogDto>>;

public sealed class GetProjectActivityLogQueryHandler
    : IRequestHandler<GetProjectActivityLogQuery, IReadOnlyList<ProjectActivityLogDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetProjectActivityLogQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProjectActivityLogDto>> Handle(
        GetProjectActivityLogQuery request,
        CancellationToken          cancellationToken)
    {
        return await _db.ProjectActivityLogs
            .Where(l => l.ProjectId == request.ProjectId && l.OrganizationId == _tenant.OrganizationId)
            .OrderByDescending(l => l.OccurredAt)
            .Select(l => new ProjectActivityLogDto(
                l.Id,
                l.UserInitials,
                l.Action,
                l.Module,
                l.OccurredAt.ToString("o")))
            .ToListAsync(cancellationToken);
    }
}

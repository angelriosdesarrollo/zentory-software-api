using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Application.Projects.Queries;

public record ProjectFileDto(
    Guid   Id,
    string Name,
    string FileType,
    string Size,
    string UploadedBy,
    string UploadedAt);

public record GetProjectFilesQuery(Guid ProjectId) : IRequest<IReadOnlyList<ProjectFileDto>>;

public sealed class GetProjectFilesQueryHandler
    : IRequestHandler<GetProjectFilesQuery, IReadOnlyList<ProjectFileDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext   _tenant;

    public GetProjectFilesQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ProjectFileDto>> Handle(
        GetProjectFilesQuery request,
        CancellationToken    cancellationToken)
    {
        return await _db.ProjectFiles
            .Where(f => f.ProjectId == request.ProjectId && f.OrganizationId == _tenant.OrganizationId)
            .OrderByDescending(f => f.UploadedAt)
            .Select(f => new ProjectFileDto(
                f.Id,
                f.Name,
                f.FileType,
                f.Size,
                f.UploadedBy,
                f.UploadedAt.ToString("yyyy-MM-dd")))
            .ToListAsync(cancellationToken);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;

namespace Zentory.Application.Projects.Queries;

public record GetProjectFileDownloadUrlQuery(Guid ProjectId, Guid FileId) : IRequest<string>;

public sealed class GetProjectFileDownloadUrlQueryHandler
    : IRequestHandler<GetProjectFileDownloadUrlQuery, string>
{
    private readonly IZentoryDbContext _db;
    private readonly IStorageService   _storage;
    private readonly ITenantContext    _tenant;

    public GetProjectFileDownloadUrlQueryHandler(IZentoryDbContext db, IStorageService storage, ITenantContext tenant)
    {
        _db      = db;
        _storage = storage;
        _tenant  = tenant;
    }

    public async Task<string> Handle(GetProjectFileDownloadUrlQuery request, CancellationToken cancellationToken)
    {
        var file = await _db.ProjectFiles.FirstOrDefaultAsync(
            f => f.Id == request.FileId && f.ProjectId == request.ProjectId
              && f.OrganizationId == _tenant.OrganizationId,
            cancellationToken);

        if (file is null)
            throw new NotFoundException("ProjectFile", request.FileId);

        return await _storage.GeneratePresignedDownloadUrlAsync(file.StorageKey, TimeSpan.FromMinutes(15), cancellationToken);
    }
}

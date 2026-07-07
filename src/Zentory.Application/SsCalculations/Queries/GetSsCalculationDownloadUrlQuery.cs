using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;

namespace Zentory.Application.SsCalculations.Queries;

public sealed record GetSsCalculationDownloadUrlQuery(Guid Id) : IRequest<string>;

public sealed class GetSsCalculationDownloadUrlQueryHandler
    : IRequestHandler<GetSsCalculationDownloadUrlQuery, string>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;
    private readonly IStorageService   _storage;

    public GetSsCalculationDownloadUrlQueryHandler(
        IZentoryDbContext db, ITenantContext tenant, IStorageService storage)
    {
        _db      = db;
        _tenant  = tenant;
        _storage = storage;
    }

    public async Task<string> Handle(GetSsCalculationDownloadUrlQuery request, CancellationToken ct)
    {
        var log = await _db.SsCalculationLogs
            .Where(s => s.Id == request.Id
                     && s.OrganizationId == _tenant.OrganizationId
                     && s.CollaboratorId == null)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("SsCalculationLog", request.Id);

        if (log.DocumentUrl is null)
            throw new NotFoundException("SsCalculationLogDocument", request.Id);

        return await _storage.GeneratePresignedDownloadUrlAsync(log.DocumentUrl, TimeSpan.FromMinutes(15), ct);
    }
}

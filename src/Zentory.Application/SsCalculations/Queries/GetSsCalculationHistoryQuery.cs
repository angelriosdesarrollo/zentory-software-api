using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.SsCalculations.DTOs;

namespace Zentory.Application.SsCalculations.Queries;

public sealed record GetSsCalculationHistoryQuery : IRequest<IReadOnlyList<SsCalculationLogDto>>;

public sealed class GetSsCalculationHistoryQueryHandler
    : IRequestHandler<GetSsCalculationHistoryQuery, IReadOnlyList<SsCalculationLogDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetSsCalculationHistoryQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<SsCalculationLogDto>> Handle(
        GetSsCalculationHistoryQuery request, CancellationToken ct)
    {
        var rows = await _db.SsCalculationLogs
            .Where(s => s.OrganizationId == _tenant.OrganizationId && s.CollaboratorId == null)
            .OrderByDescending(s => s.Period)
            .ToListAsync(ct);

        return rows.Select(s => new SsCalculationLogDto(
            s.Id, s.Period, s.Income, s.Currency, s.Result,
            s.TotalContribution, s.SmlvUsed, s.Status, s.FiledAt,
            s.DocumentFileName, s.DocumentFileSize)).ToList();
    }
}

using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;

namespace Zentory.Application.Stats.Queries;

// Dashboard financiero básico de empresa (FEATURE_PERMISSIONS §4: todos los planes): solo los indicadores del mes. El
// desglose mensual, la proyección y el YTD son la vista de flujo de caja, función studio, en GET /stats/cash-flow
// (GetCashFlowViewQuery; PLAN-0018 DEC-0104): este DTO no los lleva para nadie.

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record FinanceStatsDto(
    decimal IngresosMes,
    decimal EgresosMes,
    decimal FlujoNeto
);

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetFinanceStatsQuery(int Year, int Month) : IRequest<FinanceStatsDto>;

public sealed class GetFinanceStatsQueryHandler
    : IRequestHandler<GetFinanceStatsQuery, FinanceStatsDto>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetFinanceStatsQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<FinanceStatsDto> Handle(
        GetFinanceStatsQuery  request,
        CancellationToken     ct)
    {
        if (_tenant.LegalType != LegalType.Empresa)
            throw new ForbiddenException(ForbiddenReason.LegalTypeRequired);

        var yearEntries = await CashFlowYear.LoadAsync(_db, _tenant.OrganizationId, request.Year, request.Month, ct);
        var (ingresosMes, egresosMes) = CashFlowYear.MonthTotals(yearEntries, request.Month);

        return new FinanceStatsDto(
            IngresosMes: ingresosMes,
            EgresosMes:  egresosMes,
            FlujoNeto:   ingresosMes - egresosMes
        );
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;

namespace Zentory.Application.Stats.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record FinanceStatsDto(
    decimal                          IngresosMes,
    decimal                          EgresosMes,
    decimal                          FlujoNeto,
    decimal                          ProyectadoProxMes,
    IReadOnlyList<MonthlyFinanceDto> Monthly,
    IReadOnlyList<ProjectedMonthDto> Projected,
    decimal                          YtdIngresos,
    decimal                          YtdEgresos,
    decimal                          YtdFlujo,
    decimal                          YtdMargenPromedio
);

public record MonthlyFinanceDto(string Month, decimal Income, decimal Expenses);

public record ProjectedMonthDto(string Month, decimal Value, string Note);

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetFinanceStatsQuery(int Year, int Month) : IRequest<FinanceStatsDto>;

public sealed class GetFinanceStatsQueryHandler
    : IRequestHandler<GetFinanceStatsQuery, FinanceStatsDto>
{
    private static readonly string[] MonthNames =
    [
        "Ene", "Feb", "Mar", "Abr", "May", "Jun",
        "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"
    ];

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

        var oid = _tenant.OrganizationId;

        // Todas las entradas del año hasta el mes consultado (inclusive)
        var yearEntries = await _db.CashFlowEntries
            .Where(e => e.OrganizationId == oid
                     && e.TransactionDate.Year == request.Year
                     && e.TransactionDate.Month <= request.Month)
            .Select(e => new
            {
                e.TransactionDate.Month,
                e.Type,
                e.Amount
            })
            .ToListAsync(ct);

        // ── Mes actual ────────────────────────────────────────────────────────
        var currentMonth = yearEntries.Where(e => e.Month == request.Month).ToList();
        var ingresosMes  = currentMonth.Where(e => e.Type == "income").Sum(e => e.Amount);
        var egresosMes   = currentMonth.Where(e => e.Type == "expense").Sum(e => e.Amount);
        var flujoNeto    = ingresosMes - egresosMes;

        // ── Proyección próximo mes: promedio de ingresos de los últimos 3 meses ──
        var last3Months = Enumerable.Range(1, 3)
            .Select(i =>
            {
                var m = request.Month - i;
                var y = request.Year;
                if (m <= 0) { m += 12; y--; }
                return (Year: y, Month: m);
            })
            .ToList();

        // Para el cálculo de proyección usamos el promedio de los 3 meses previos del mismo año
        var last3Incomes = yearEntries
            .Where(e => e.Type == "income" && last3Months.Any(lm => lm.Year == request.Year && lm.Month == e.Month))
            .ToList();

        var proyectadoProxMes = last3Incomes.Any()
            ? last3Incomes.Sum(e => e.Amount) / 3m
            : ingresosMes;

        // ── Monthly breakdown (todos los meses del año hasta el mes actual) ──
        var monthly = Enumerable.Range(1, request.Month)
            .Select(m =>
            {
                var inc = yearEntries.Where(e => e.Month == m && e.Type == "income").Sum(e => e.Amount);
                var exp = yearEntries.Where(e => e.Month == m && e.Type == "expense").Sum(e => e.Amount);
                return new MonthlyFinanceDto(MonthNames[m - 1], inc, exp);
            })
            .ToList();

        // ── Projected: próximos 3 meses ───────────────────────────────────────
        var projected = Enumerable.Range(1, 3)
            .Select(i =>
            {
                var m = (request.Month + i - 1) % 12 + 1;
                return new ProjectedMonthDto(MonthNames[m - 1], proyectadoProxMes, "Estimado basado en promedio");
            })
            .ToList();

        // ── YTD ───────────────────────────────────────────────────────────────
        var ytdIngresos = yearEntries.Where(e => e.Type == "income").Sum(e => e.Amount);
        var ytdEgresos  = yearEntries.Where(e => e.Type == "expense").Sum(e => e.Amount);
        var ytdFlujo    = ytdIngresos - ytdEgresos;

        // Margen promedio mensual: promedio de (income - expense) / income por mes con income > 0
        var monthlyMargens = Enumerable.Range(1, request.Month)
            .Select(m =>
            {
                var inc = yearEntries.Where(e => e.Month == m && e.Type == "income").Sum(e => e.Amount);
                var exp = yearEntries.Where(e => e.Month == m && e.Type == "expense").Sum(e => e.Amount);
                return inc > 0 ? (inc - exp) / inc * 100m : 0m;
            })
            .ToList();

        var ytdMargenPromedio = monthlyMargens.Any(m => m != 0m)
            ? monthlyMargens.Where(m => m != 0m).Average()
            : 0m;

        return new FinanceStatsDto(
            IngresosMes:       ingresosMes,
            EgresosMes:        egresosMes,
            FlujoNeto:         flujoNeto,
            ProyectadoProxMes: proyectadoProxMes,
            Monthly:           monthly,
            Projected:         projected,
            YtdIngresos:       ytdIngresos,
            YtdEgresos:        ytdEgresos,
            YtdFlujo:          ytdFlujo,
            YtdMargenPromedio: Math.Round(ytdMargenPromedio, 1)
        );
    }
}

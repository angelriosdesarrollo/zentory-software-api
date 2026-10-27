using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Domain.Constants;

namespace Zentory.Application.Stats.Queries;

// Vista de flujo de caja (PLAN-0018 RF-75 b, c; DEC-0104; FEATURE_PERMISSIONS §4 "Flujo de caja / runway" y "Flujo
// proyectado + YTD"): función studio de empresa, en GET /stats/cash-flow con EmpresaStudio. El cálculo es el que antes
// viajaba dentro de GET /stats/finance, sin cambios; /stats/finance conserva solo los indicadores básicos del mes.

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CashFlowViewDto(
    IReadOnlyList<MonthlyFinanceDto> Monthly,
    IReadOnlyList<ProjectedMonthDto> Projected,
    decimal                          ProyectadoProxMes,
    decimal                          YtdIngresos,
    decimal                          YtdEgresos,
    decimal                          YtdFlujo,
    decimal                          YtdMargenPromedio
);

public record MonthlyFinanceDto(string Month, decimal Income, decimal Expenses);

public record ProjectedMonthDto(string Month, decimal Value, string Note);

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetCashFlowViewQuery(int Year, int Month) : IRequest<CashFlowViewDto>;

public sealed class GetCashFlowViewQueryHandler : IRequestHandler<GetCashFlowViewQuery, CashFlowViewDto>
{
    private static readonly string[] MonthNames =
    [
        "Ene", "Feb", "Mar", "Abr", "May", "Jun",
        "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"
    ];

    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetCashFlowViewQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<CashFlowViewDto> Handle(GetCashFlowViewQuery request, CancellationToken ct)
    {
        if (_tenant.LegalType != LegalType.Empresa)
            throw new ForbiddenException(ForbiddenReason.LegalTypeRequired);

        var yearEntries = await CashFlowYear.LoadAsync(_db, _tenant.OrganizationId, request.Year, request.Month, ct);
        var (ingresosMes, _) = CashFlowYear.MonthTotals(yearEntries, request.Month);

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
                var (inc, exp) = CashFlowYear.MonthTotals(yearEntries, m);
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
                var (inc, exp) = CashFlowYear.MonthTotals(yearEntries, m);
                return inc > 0 ? (inc - exp) / inc * 100m : 0m;
            })
            .ToList();

        var ytdMargenPromedio = monthlyMargens.Any(m => m != 0m)
            ? monthlyMargens.Where(m => m != 0m).Average()
            : 0m;

        return new CashFlowViewDto(
            Monthly:           monthly,
            Projected:         projected,
            ProyectadoProxMes: proyectadoProxMes,
            YtdIngresos:       ytdIngresos,
            YtdEgresos:        ytdEgresos,
            YtdFlujo:          ytdFlujo,
            YtdMargenPromedio: Math.Round(ytdMargenPromedio, 1)
        );
    }
}

// Entradas de flujo de caja del año hasta el mes consultado (inclusive) y sus totales por mes. Lo comparten
// /stats/finance (mes actual) y /stats/cash-flow (vista studio): una sola lectura y la misma suma en los dos.
internal static class CashFlowYear
{
    internal sealed record Entry(int Month, string Type, decimal Amount);

    public static async Task<List<Entry>> LoadAsync(
        IZentoryDbContext db, Guid organizationId, int year, int month, CancellationToken ct)
        => await db.CashFlowEntries
            .Where(e => e.OrganizationId == organizationId
                     && e.TransactionDate.Year == year
                     && e.TransactionDate.Month <= month)
            .Select(e => new Entry(e.TransactionDate.Month, e.Type, e.Amount))
            .ToListAsync(ct);

    public static (decimal Income, decimal Expenses) MonthTotals(IReadOnlyCollection<Entry> entries, int month)
        => (entries.Where(e => e.Month == month && e.Type == "income").Sum(e => e.Amount),
            entries.Where(e => e.Month == month && e.Type == "expense").Sum(e => e.Amount));
}

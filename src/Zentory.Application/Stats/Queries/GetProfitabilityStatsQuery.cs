using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Projects;
using Zentory.Domain.Entities;

namespace Zentory.Application.Stats.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record ProfitabilityStatsDto(
    IReadOnlyList<ProjectProfitabilityDto>  Projects,
    IReadOnlyList<MonthlyProfitabilityDto>  Monthly,
    string                                  Currency
);

public record ProjectProfitabilityDto(
    string  Id,
    string  Name,
    string  Client,
    decimal Revenue,
    decimal CostHours,
    decimal Overhead,
    decimal Profitability,
    int     Margin,
    int     HealthScore,
    string  HealthStatus,
    bool    IsNegative
);

public record MonthlyProfitabilityDto(string Month, decimal Revenue, decimal Cost, int Margin);

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetProfitabilityStatsQuery : IRequest<ProfitabilityStatsDto>;

public sealed class GetProfitabilityStatsQueryHandler
    : IRequestHandler<GetProfitabilityStatsQuery, ProfitabilityStatsDto>
{
    private static readonly string[] MonthNames =
    [
        "Ene", "Feb", "Mar", "Abr", "May", "Jun",
        "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"
    ];

    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetProfitabilityStatsQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<ProfitabilityStatsDto> Handle(
        GetProfitabilityStatsQuery request,
        CancellationToken          ct)
    {
        var oid = _tenant.OrganizationId;

        // ── Proyectos activos con cliente ─────────────────────────────────────
        var projects = await _db.Projects
            .Where(p => p.OrganizationId == oid && p.Status == ProjectStatus.Active)
            .Join(_db.Clients,
                  p => p.ClientId,
                  c => c.Id,
                  (p, c) => new { Project = p, ClientName = c.Name })
            .ToListAsync(ct);

        // ── Facturas por proyecto (excluye draft y canceladas) ────────────────
        var projectIds = projects.Select(x => x.Project.Id).ToList();

        var invoiceRevenue = await _db.Invoices
            .Where(i => i.OrganizationId == oid
                     && i.ProjectId != null
                     && projectIds.Contains(i.ProjectId!.Value)
                     && i.Status != "draft"
                     && i.Status != "cancelled")
            .GroupBy(i => i.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Revenue = g.Sum(i => i.Total) })
            .ToListAsync(ct);

        // ── Costo de horas por proyecto ───────────────────────────────────────
        var timeCost = await _db.TimeEntries
            .Where(t => t.OrganizationId == oid
                     && projectIds.Contains(t.ProjectId))
            .GroupBy(t => t.ProjectId)
            .Select(g => new { ProjectId = g.Key, Cost = g.Sum(t => t.Hours * t.RateCost) })
            .ToListAsync(ct);

        // ── Construir DTOs por proyecto ───────────────────────────────────────
        var projectDtos = projects.Select(x =>
        {
            var p          = x.Project;
            var revenue    = invoiceRevenue.FirstOrDefault(r => r.ProjectId == p.Id)?.Revenue ?? 0m;
            var costHours  = timeCost.FirstOrDefault(t => t.ProjectId == p.Id)?.Cost ?? 0m;
            var overhead   = revenue * 0.08m;
            var profit     = revenue - costHours - overhead;
            var margin     = revenue > 0 ? (int)Math.Round(profit / revenue * 100) : 0;

            var (_, healthScore, healthStatus) = ProjectHealthHelper.Compute(p.HoursUsed, p.HoursTotal);

            return new ProjectProfitabilityDto(
                Id:           p.Id.ToString(),
                Name:         p.Name,
                Client:       x.ClientName,
                Revenue:      revenue,
                CostHours:    costHours,
                Overhead:     overhead,
                Profitability: profit,
                Margin:       margin,
                HealthScore:  healthScore,
                HealthStatus: healthStatus,
                IsNegative:   profit < 0
            );
        }).ToList();

        // ── Monthly (últimos 6 meses de CashFlowEntries) ─────────────────────
        var now     = DateTime.UtcNow;
        var cutoff  = new DateOnly(now.Year, now.Month, 1).AddMonths(-5);

        var cashEntries = await _db.CashFlowEntries
            .Where(e => e.OrganizationId == oid
                     && e.TransactionDate >= cutoff)
            .Select(e => new
            {
                e.TransactionDate.Year,
                e.TransactionDate.Month,
                e.Type,
                e.Amount
            })
            .ToListAsync(ct);

        var monthly = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var date    = new DateOnly(now.Year, now.Month, 1).AddMonths(i - 5);
                var mYear   = date.Year;
                var mMonth  = date.Month;
                var rev     = cashEntries.Where(e => e.Year == mYear && e.Month == mMonth && e.Type == "income").Sum(e => e.Amount);
                var cost    = cashEntries.Where(e => e.Year == mYear && e.Month == mMonth && e.Type == "expense").Sum(e => e.Amount);
                var mMargin = rev > 0 ? (int)Math.Round((rev - cost) / rev * 100) : 0;
                return new MonthlyProfitabilityDto(MonthNames[mMonth - 1], rev, cost, mMargin);
            })
            .ToList();

        return new ProfitabilityStatsDto(
            Projects: projectDtos,
            Monthly:  monthly,
            Currency: "USD"
        );
    }
}

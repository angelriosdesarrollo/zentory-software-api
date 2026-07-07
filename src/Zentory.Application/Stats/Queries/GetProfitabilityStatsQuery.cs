using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Projects;
using Zentory.Domain.Constants;
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
    private readonly ProjectExpenseStore _expenses;

    public GetProfitabilityStatsQueryHandler(IZentoryDbContext db, ITenantContext tenant, ProjectExpenseStore expenses)
    {
        _db       = db;
        _tenant   = tenant;
        _expenses = expenses;
    }

    public async Task<ProfitabilityStatsDto> Handle(
        GetProfitabilityStatsQuery request,
        CancellationToken          ct)
    {
        if (_tenant.LegalType != LegalType.Empresa)
            throw new ForbiddenException(ForbiddenReason.LegalTypeRequired);
        if (_tenant.Plan != Plan.Studio)
            throw new ForbiddenException(ForbiddenReason.PlanRequired, Plan.Studio);

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
        // Excluye 'billed': son horas cuyo costo real ya quedó registrado como gasto al
        // aprobar la cuenta de cobro del colaborador (ver ReviewPayoutInvoiceCommand) — la
        // cuenta de cobro tiene prioridad sobre el estimado, así que estas horas no se cuentan
        // dos veces aquí.
        var timeCost = await _db.TimeEntries
            .Where(t => t.OrganizationId == oid
                     && projectIds.Contains(t.ProjectId)
                     && t.Status != "billed")
            .GroupBy(t => t.ProjectId)
            .Select(g => new { ProjectId = g.Key, Cost = g.Sum(t => t.Hours * t.RateCost) })
            .ToListAsync(ct);

        // ── Pagos recibidos y avance de tareas por proyecto (factores del Health Score) ──
        var paidByProject = await _db.Invoices
            .Where(i => i.OrganizationId == oid && i.ProjectId != null && projectIds.Contains(i.ProjectId!.Value))
            .GroupBy(i => i.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Paid = g.Sum(i => i.AmountPaid) })
            .ToListAsync(ct);

        var taskCounts = await _db.ProjectTasks
            .Where(t => t.OrganizationId == oid && t.DeletedAt == null && projectIds.Contains(t.ProjectId))
            .GroupBy(t => t.ProjectId)
            .Select(g => new { ProjectId = g.Key, Total = g.Count(), Done = g.Count(t => t.Status == "done") })
            .ToListAsync(ct);

        // ── Construir DTOs por proyecto ───────────────────────────────────────
        var projectDtos = projects.Select(x =>
        {
            var p          = x.Project;
            var revenue    = invoiceRevenue.FirstOrDefault(r => r.ProjectId == p.Id)?.Revenue ?? 0m;
            var estimatedCost = timeCost.FirstOrDefault(t => t.ProjectId == p.Id)?.Cost ?? 0m;
            // Solo los gastos generados por cuentas de cobro aprobadas (Source ==
            // "payout_invoice") cuentan como "costo de equipo" — un gasto manual de licencias
            // o viáticos no es costo de horas y no debería mezclarse aquí.
            var realizedPayoutCost = _expenses.GetByProject(p.Id)
                .Where(e => e.Status == "aprobado" && e.Source == "payout_invoice")
                .Sum(e => e.Amount);
            var costHours = estimatedCost + realizedPayoutCost;
            var overhead   = revenue * 0.08m;
            var profit     = revenue - costHours - overhead;
            var margin     = revenue > 0 ? (int)Math.Round(profit / revenue * 100) : 0;

            var paid  = paidByProject.FirstOrDefault(r => r.ProjectId == p.Id)?.Paid ?? 0m;
            var tasks = taskCounts.FirstOrDefault(t => t.ProjectId == p.Id);

            var health = ProjectHealthHelper.Compute(new ProjectHealthHelper.HealthInput(
                HoursUsed:     p.HoursUsed,
                HoursTotal:    p.HoursTotal,
                ContractValue: p.ContractValue,
                AmountPaid:    paid,
                StartDate:     p.StartDate,
                EndDate:       p.EndDate,
                TasksTotal:    tasks?.Total ?? 0,
                TasksDone:     tasks?.Done ?? 0));

            return new ProjectProfitabilityDto(
                Id:           p.Id.ToString(),
                Name:         p.Name,
                Client:       x.ClientName,
                Revenue:      revenue,
                CostHours:    costHours,
                Overhead:     overhead,
                Profitability: profit,
                Margin:       margin,
                HealthScore:  health.HealthScore,
                HealthStatus: health.HealthStatus,
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

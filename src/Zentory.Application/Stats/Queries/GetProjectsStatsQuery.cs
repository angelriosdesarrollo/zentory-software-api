using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;

namespace Zentory.Application.Stats.Queries;

// ── DTO ───────────────────────────────────────────────────────────────────────

public record ProjectsStatsDto(
    int     Active,
    int     AtRisk,
    decimal TotalBudget,
    decimal HoursThisMonth,
    string  Currency
);

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetProjectsStatsQuery : IRequest<ProjectsStatsDto>;

public sealed class GetProjectsStatsQueryHandler
    : IRequestHandler<GetProjectsStatsQuery, ProjectsStatsDto>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetProjectsStatsQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<ProjectsStatsDto> Handle(
        GetProjectsStatsQuery request,
        CancellationToken     ct)
    {
        var oid = _tenant.OrganizationId;
        var now = DateTime.UtcNow;

        var projects = await _db.Projects
            .Where(p => p.OrganizationId == oid)
            .Select(p => new
            {
                p.Status,
                p.ContractValue,
                p.HoursUsed,
                p.HoursTotal
            })
            .ToListAsync(ct);

        var active      = projects.Count(p => p.Status == ProjectStatus.Active);
        var totalBudget = projects
            .Where(p => p.Status == ProjectStatus.Active)
            .Sum(p => p.ContractValue);

        // Proyecto en riesgo: consumió más del 85% de horas o superó el presupuesto
        var atRisk = projects.Count(p =>
            p.Status == ProjectStatus.Active &&
            p.HoursTotal > 0 &&
            (decimal)p.HoursUsed / p.HoursTotal > 0.85m);

        var hoursThisMonth = await _db.TimeEntries
            .Where(t => t.OrganizationId == oid
                     && t.Date.Year  == now.Year
                     && t.Date.Month == now.Month)
            .SumAsync(t => t.Hours, ct);

        return new ProjectsStatsDto(
            Active:         active,
            AtRisk:         atRisk,
            TotalBudget:    totalBudget,
            HoursThisMonth: hoursThisMonth,
            Currency:       "USD"
        );
    }
}

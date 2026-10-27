using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Stats.Queries;
using Zentory.API.Authorization;
using Zentory.API.Extensions;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/stats")]
public sealed class StatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public StatsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/stats/finance?year=2026&amp;month=6</summary>
    // PLAN-0018 RF-86 c (D-41): indicadores financieros del mes solo para owner y admin.
    [Authorize(Policy = PolicyNames.RequireFinancialAccess)]
    [HttpGet("finance")]
    public async Task<IActionResult> Finance(
        [FromQuery] int year  = 0,
        [FromQuery] int month = 0,
        CancellationToken ct  = default)
    {
        if (year  == 0) year  = DateTime.UtcNow.Year;
        if (month == 0) month = DateTime.UtcNow.Month;

        var result = await _mediator.Send(new GetFinanceStatsQuery(year, month), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/stats/cash-flow?year=2026&amp;month=6</summary>
    // Vista de flujo de caja (mensual, proyección, YTD): función studio de empresa (FEATURE_PERMISSIONS §4; PLAN-0018
    // RF-75 b, c; DEC-0104). Tras bajar de studio responde 403 PLAN_REQUIRED también en lectura. Solo owner y admin, como
    // /stats/finance (RF-86; deuda de DEC-0105). Las policies del método y de la clase se combinan en una sola evaluación:
    // con la precedencia de RF-58, si fallan plan y rol a la vez gana PLAN_REQUIRED.
    [Authorize(Policy = PolicyNames.EmpresaStudio)]
    [Authorize(Policy = PolicyNames.RequireFinancialAccess)]
    [HttpGet("cash-flow")]
    public async Task<IActionResult> CashFlow(
        [FromQuery] int year  = 0,
        [FromQuery] int month = 0,
        CancellationToken ct  = default)
    {
        if (year  == 0) year  = DateTime.UtcNow.Year;
        if (month == 0) month = DateTime.UtcNow.Month;

        var result = await _mediator.Send(new GetCashFlowViewQuery(year, month), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/stats/projects</summary>
    // Abierto a cualquier miembro; los montos de projects e invoices los afinan B y C con IFinancialVisibility (R-E1).
    [AnyOrgMember]
    [HttpGet("projects")]
    public async Task<IActionResult> Projects(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectsStatsQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/stats/invoices</summary>
    [AnyOrgMember]
    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetInvoicesStatsQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/stats/profitability</summary>
    // PLAN-0018 RF-86 c (D-41): rentabilidad solo para owner y admin.
    [Authorize(Policy = PolicyNames.RequireFinancialAccess)]
    [HttpGet("profitability")]
    public async Task<IActionResult> Profitability(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProfitabilityStatsQuery(), ct);
        return Ok(result);
    }
}

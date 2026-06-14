using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Stats.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/stats")]
public sealed class StatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public StatsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/stats/finance?year=2026&amp;month=6</summary>
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

    /// <summary>GET /api/v1/stats/projects</summary>
    [HttpGet("projects")]
    public async Task<IActionResult> Projects(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectsStatsQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/stats/invoices</summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetInvoicesStatsQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/stats/profitability</summary>
    [HttpGet("profitability")]
    public async Task<IActionResult> Profitability(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProfitabilityStatsQuery(), ct);
        return Ok(result);
    }
}

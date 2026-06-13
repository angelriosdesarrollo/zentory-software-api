using MediatR;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Master.Queries;
using Zentory.Application.Plans.Queries;

namespace Zentory.API.Controllers.V1;

/// <summary>
/// Endpoints públicos sin autenticación: planes de pricing, tasas de seguridad social,
/// categorías de gastos. Los datos provienen de las tablas master del sistema.
/// </summary>
[ApiController]
[Route("api/v1/public")]
public sealed class PublicController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/public/plans — planes de precios con features, límites y marketing copy</summary>
    [HttpGet("plans")]
    [ResponseCache(Duration = 300)] // 5 min — los precios no cambian con frecuencia
    public async Task<IActionResult> GetPlans(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPlansQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/public/ss-rules — tasas de seguridad social por país y año</summary>
    [HttpGet("ss-rules")]
    [ResponseCache(Duration = 86400)] // 24 h — las tasas cambian máximo una vez al año
    public async Task<IActionResult> GetSsRules(
        [FromQuery] string countryCode = "CO",
        [FromQuery] short? year        = null,
        CancellationToken  ct          = default)
    {
        var result = await _mediator.Send(new GetSsRulesQuery(countryCode, year), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/public/expense-categories — categorías de gastos e ingresos</summary>
    [HttpGet("expense-categories")]
    [ResponseCache(Duration = 3600)] // 1 h
    public async Task<IActionResult> GetExpenseCategories(
        [FromQuery] string? type = null,
        CancellationToken   ct   = default)
    {
        var result = await _mediator.Send(new GetExpenseCategoriesQuery(type), ct);
        return Ok(result);
    }
}

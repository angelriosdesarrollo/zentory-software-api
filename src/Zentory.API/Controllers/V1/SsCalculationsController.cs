using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.SsCalculations.Commands;
using Zentory.Application.SsCalculations.Queries;

namespace Zentory.API.Controllers.V1;

// PILA personal — "siempre gratis para todos" (freelance y empresa, cualquier plan).
// A diferencia de PilaVerificationsController (EmpresaStudio, verificación de equipo),
// este controlador no lleva [Authorize(Policy = ...)] a propósito.
[ApiController]
[Authorize]
[Route("api/v1/me/ss-calculations")]
public sealed class SsCalculationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SsCalculationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/me/ss-calculations — historial de cálculos propios</summary>
    [HttpGet]
    public async Task<IActionResult> GetHistory(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSsCalculationHistoryQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/me/ss-calculations — guarda/actualiza el cálculo de un período</summary>
    [HttpPost]
    public async Task<IActionResult> Log([FromBody] LogSsCalculationCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/me/ss-calculations/upload-url — URL prefirmada para subir el comprobante</summary>
    [HttpPost("upload-url")]
    public async Task<IActionResult> GetUploadUrl(
        [FromBody] GetSsCalculationUploadUrlCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/me/ss-calculations/{id}/mark-filed — marca el período como radicado</summary>
    [HttpPatch("{id:guid}/mark-filed")]
    public async Task<IActionResult> MarkFiled(
        Guid id, [FromBody] MarkSsCalculationFiledCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/me/ss-calculations/{id}/download-url</summary>
    [HttpGet("{id:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid id, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetSsCalculationDownloadUrlQuery(id), ct);
        return Ok(new { url });
    }
}

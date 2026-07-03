using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.CollaboratorPortal.Commands;
using Zentory.Application.CollaboratorPortal.Queries;

namespace Zentory.API.Controllers.V1;

// Requiere una sesión de portal ya establecida (JWT del esquema "CollaboratorScheme",
// emitido por /public/collaborator-portal/exchange) — separado del [Authorize] por
// defecto que usan los controllers de empresa.
[ApiController]
[Authorize(AuthenticationSchemes = "CollaboratorScheme", Policy = "CollaboratorAuth")]
[Route("api/v1/collaborator-portal")]
public sealed class CollaboratorSelfServiceController : ControllerBase
{
    private readonly IMediator _mediator;

    public CollaboratorSelfServiceController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/collaborator-portal/switch — cambiar de organización activa</summary>
    [HttpPost("switch")]
    public async Task<IActionResult> Switch(
        [FromBody] SwitchActiveCollaboratorCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/collaborator-portal/me — datos del colaborador activo</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOwnProfileQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/collaborator-portal/pila — historial PILA propio</summary>
    [HttpGet("pila")]
    public async Task<IActionResult> GetPilaHistory(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOwnPilaHistoryQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/collaborator-portal/pila/upload-url</summary>
    [HttpPost("pila/upload-url")]
    public async Task<IActionResult> GetPilaUploadUrl(
        [FromBody] GetOwnPilaUploadUrlCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/collaborator-portal/pila/confirm</summary>
    [HttpPost("pila/confirm")]
    public async Task<IActionResult> ConfirmPila(
        [FromBody] UploadOwnPilaEvidenceCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>GET /api/v1/collaborator-portal/pila/{id}/download-url</summary>
    [HttpGet("pila/{id:guid}/download-url")]
    public async Task<IActionResult> GetPilaDownloadUrl(Guid id, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetOwnPilaDownloadUrlQuery(id), ct);
        return Ok(new { url });
    }

    /// <summary>GET /api/v1/collaborator-portal/payout-invoices — historial de cuentas de cobro propio</summary>
    [HttpGet("payout-invoices")]
    public async Task<IActionResult> GetPayoutInvoiceHistory(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOwnPayoutInvoiceHistoryQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/collaborator-portal/payout-invoices/upload-url</summary>
    [HttpPost("payout-invoices/upload-url")]
    public async Task<IActionResult> GetPayoutInvoiceUploadUrl(
        [FromBody] GetOwnPayoutInvoiceUploadUrlCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/collaborator-portal/payout-invoices/confirm</summary>
    [HttpPost("payout-invoices/confirm")]
    public async Task<IActionResult> ConfirmPayoutInvoice(
        [FromBody] ConfirmOwnPayoutInvoiceCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>GET /api/v1/collaborator-portal/payout-invoices/{id}/download-url — descarga el borrador generado por la empresa</summary>
    [HttpGet("payout-invoices/{id:guid}/download-url")]
    public async Task<IActionResult> GetPayoutInvoiceDownloadUrl(Guid id, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetOwnPayoutInvoiceDownloadUrlQuery(id), ct);
        return Ok(new { url });
    }

    /// <summary>POST /api/v1/collaborator-portal/payout-invoices/{id}/sign — firma electrónica (nombre + checkbox), regenera el PDF</summary>
    [HttpPost("payout-invoices/{id:guid}/sign")]
    public async Task<IActionResult> SignPayoutInvoice(
        Guid id, [FromBody] SignPayoutInvoiceRequest body, CancellationToken ct = default)
    {
        await _mediator.Send(new SignOwnPayoutInvoiceCommand(id, body.SignedByName), ct);
        return NoContent();
    }
}

public sealed record SignPayoutInvoiceRequest(string SignedByName);

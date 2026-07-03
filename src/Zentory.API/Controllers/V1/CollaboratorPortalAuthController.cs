using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Zentory.Application.CollaboratorPortal.Commands;

namespace Zentory.API.Controllers.V1;

// Público, sin [Authorize] — es el punto de entrada al portal (Camino B: magic link
// propio; Camino A: canje de un token de solicitud puntual ya existente).
[ApiController]
[EnableRateLimiting("CollaboratorPortalAuthPolicy")]
[Route("api/v1/public/collaborator-portal")]
public sealed class CollaboratorPortalAuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public CollaboratorPortalAuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/public/collaborator-portal/request-link — Camino B (email + cédula)</summary>
    [HttpPost("request-link")]
    public async Task<IActionResult> RequestLink(
        [FromBody] RequestCollaboratorAccessLinkCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command, ct);
        return Ok(); // siempre 200, sin importar si hubo match — anti-enumeración
    }

    /// <summary>POST /api/v1/public/collaborator-portal/exchange — canje único (magic_link | pila_request | payout_invoice_request)</summary>
    [HttpPost("exchange")]
    public async Task<IActionResult> Exchange(
        [FromBody] ExchangeCollaboratorTokenCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}

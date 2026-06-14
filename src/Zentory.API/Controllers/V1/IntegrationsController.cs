using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Integrations.Commands;
using Zentory.Application.Integrations.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IntegrationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/integrations — lista de integraciones disponibles con estado de conexión del tenant</summary>
    [HttpGet]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetIntegrationsQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/integrations/{id}/connect — conectar una integración</summary>
    [HttpPost("{id}/connect")]
    public async Task<IActionResult> Connect(
        string id,
        [FromBody] ConnectIntegrationCommand command,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { IntegrationId = id }, ct);
        return Ok(result);
    }

    /// <summary>DELETE /api/v1/integrations/{id}/connect — desconectar una integración</summary>
    [HttpDelete("{id}/connect")]
    public async Task<IActionResult> Disconnect(string id, CancellationToken ct = default)
    {
        await _mediator.Send(new DisconnectIntegrationCommand(id), ct);
        return NoContent();
    }
}

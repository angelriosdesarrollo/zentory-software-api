using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Integrations.Commands;
using Zentory.Application.Integrations.Queries;
using Zentory.API.Authorization;
using Zentory.API.Extensions;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IntegrationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/integrations — lista de integraciones disponibles con estado de conexión del tenant</summary>
    [AnyOrgMember]
    [HttpGet]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetIntegrationsQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/integrations/{id}/connect — conectar una integración</summary>
    // REV-0005 H-6: conectar y desconectar es Configuración (RF-59 c, RF-66 c; FEATURE_PERMISSIONS §2.11): owner o admin.
    // El GET sigue abierto a cualquier miembro, como los GET de perfil y ajustes: solo lista el estado de conexión.
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
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
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
    [HttpDelete("{id}/connect")]
    public async Task<IActionResult> Disconnect(string id, CancellationToken ct = default)
    {
        await _mediator.Send(new DisconnectIntegrationCommand(id), ct);
        return NoContent();
    }
}

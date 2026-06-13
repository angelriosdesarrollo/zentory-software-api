using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Clients.Commands;
using Zentory.Application.Clients.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/clients")]
public sealed class ClientsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ClientsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/clients — lista con búsqueda opcional</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetClientsQuery(search), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/clients/{id} — obtener cliente por id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetClientByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/clients — crear cliente</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>PATCH /api/v1/clients/{id} — actualizar cliente parcialmente</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClientCommand command, CancellationToken ct = default)
    {
        // Bind the route id into the command
        var cmd = command with { Id = id };
        var result = await _mediator.Send(cmd, ct);
        return Ok(result);
    }

    /// <summary>DELETE /api/v1/clients/{id} — soft delete</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteClientCommand(id), ct);
        return NoContent();
    }
}

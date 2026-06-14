using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Proposals.Commands;
using Zentory.Application.Proposals.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/proposals")]
public sealed class ProposalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProposalsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/proposals</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status   = null,
        [FromQuery] Guid?   clientId = null,
        [FromQuery] string? search   = null,
        CancellationToken   ct       = default)
    {
        var result = await _mediator.Send(new GetProposalsQuery(status, clientId, search), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/proposals/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProposalByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/proposals — crear propuesta</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProposalCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>PATCH /api/v1/proposals/{id} — actualizar contenido</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProposalCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    /// <summary>PUT /api/v1/proposals/{id}/sections — reemplaza todas las secciones</summary>
    [HttpPut("{id:guid}/sections")]
    public async Task<IActionResult> SaveSections(Guid id, [FromBody] SaveProposalSectionsCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    /// <summary>PUT /api/v1/proposals/{id}/items — reemplaza todos los ítems de precio</summary>
    [HttpPut("{id:guid}/items")]
    public async Task<IActionResult> SaveItems(Guid id, [FromBody] SaveProposalItemsCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/proposals/{id}/send — marcar como enviada</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new SendProposalCommand(id), ct);
        return NoContent();
    }

    /// <summary>PATCH /api/v1/proposals/{id}/status — cambiar estado (override administrativo)</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeProposalStatusCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/proposals/{id}/convert-to-project — convertir propuesta aceptada en proyecto</summary>
    [HttpPost("{id:guid}/convert-to-project")]
    public async Task<IActionResult> ConvertToProject(Guid id, [FromBody] ConvertProposalToProjectCommand command, CancellationToken ct = default)
    {
        var projectId = await _mediator.Send(command with { Id = id }, ct);
        return Created(string.Empty, new { projectId });
    }

    /// <summary>DELETE /api/v1/proposals/{id} — soft delete</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteProposalCommand(id), ct);
        return NoContent();
    }
}

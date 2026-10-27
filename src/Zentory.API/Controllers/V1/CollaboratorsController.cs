using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Collaborators.Commands;
using Zentory.Application.Collaborators.Queries;
using Zentory.API.Authorization;
using Zentory.API.Extensions;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize(Policy = PolicyNames.EmpresaStudio)]
[Route("api/v1/collaborators")]
// PLAN-0018 RF-86 d (D-41, C-14): no se cierra entero porque el member usa la lista para asignar tareas y registrar horas.
// Lectura con [AnyOrgMember] (los handlers omiten HourlyRate y MonthlyRate para member vía IFinancialVisibility); crear,
// editar (también solo la tarifa) y eliminar con RequireOrgAdmin.
public sealed class CollaboratorsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CollaboratorsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/collaborators</summary>
    [AnyOrgMember]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken   ct     = default)
    {
        var result = await _mediator.Send(new GetCollaboratorsQuery(search, status), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/collaborators/{id}</summary>
    [AnyOrgMember]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetCollaboratorByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/collaborators — crear colaborador</summary>
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCollaboratorCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>PATCH /api/v1/collaborators/{id}</summary>
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCollaboratorCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    /// <summary>DELETE /api/v1/collaborators/{id} — soft delete</summary>
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteCollaboratorCommand(id), ct);
        return NoContent();
    }
}

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects — lista con filtros opcionales</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search   = null,
        [FromQuery] string? status   = null,
        [FromQuery] Guid?   clientId = null,
        CancellationToken   ct       = default)
    {
        var result = await _mediator.Send(new GetProjectsQuery(search, status, clientId), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/projects/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects — crear proyecto</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>PATCH /api/v1/projects/{id} — actualizar proyecto</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/projects/{id}/status — cambiar estado</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeProjectStatusCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/projects/{id} — soft delete</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteProjectCommand(id), ct);
        return NoContent();
    }
}

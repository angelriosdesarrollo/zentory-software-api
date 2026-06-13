using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.TimeEntries.Commands;
using Zentory.Application.TimeEntries.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/time-entries")]
public sealed class TimeEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public TimeEntriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/time-entries</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? from           = null,
        [FromQuery] DateTime? to             = null,
        [FromQuery] string?   status         = null,
        [FromQuery] Guid?     projectId      = null,
        [FromQuery] Guid?     collaboratorId = null,
        CancellationToken     ct             = default)
    {
        var result = await _mediator.Send(new GetTimeEntriesQuery(from, to, status, projectId, collaboratorId), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/time-entries/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetTimeEntryByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/time-entries — registrar horas</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTimeEntryCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>PATCH /api/v1/time-entries/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTimeEntryCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { Id = id }, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/time-entries/{id}/approve — aprobar entrada pendiente</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new ApproveTimeEntryCommand(id), ct);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/time-entries/{id} — soft delete</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteTimeEntryCommand(id), ct);
        return NoContent();
    }
}

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.CashFlow.Commands;
using Zentory.Application.CashFlow.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/cash-flow-entries")]
public sealed class CashFlowEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public CashFlowEntriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/cash-flow-entries?year=2026&amp;month=6</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int?  year      = null,
        [FromQuery] int?  month     = null,
        [FromQuery] string? type    = null,
        [FromQuery] Guid? projectId = null,
        CancellationToken ct        = default)
    {
        var result = await _mediator.Send(
            new GetCashFlowEntriesQuery(year, month, type, projectId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/cash-flow-entries</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCashFlowEntryCommand command,
        CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(List), new { id }, new { id });
    }

    /// <summary>PATCH /api/v1/cash-flow-entries/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCashFlowEntryCommand command,
        CancellationToken ct = default)
    {
        var cmd = command with { Id = id };
        await _mediator.Send(cmd, ct);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/cash-flow-entries/{id}</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteCashFlowEntryCommand(id), ct);
        return NoContent();
    }
}

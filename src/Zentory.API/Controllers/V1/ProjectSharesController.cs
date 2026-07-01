using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.ProjectShares.Commands;
using Zentory.Application.ProjectShares.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/share")]
public sealed class ProjectSharesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectSharesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects/{projectId}/share — lista de links generados para el proyecto</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectSharesQuery(projectId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects/{projectId}/share — generar nuevo link público</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId,
        [FromBody] CreateShareRequest body,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new CreateProjectShareCommand(
            projectId,
            body.Message,
            body.ExpiresAt,
            body.IncludedFileIds ?? [],
            body.IncludedDeliverableIds ?? []), ct);

        return CreatedAtAction(nameof(List), new { projectId }, result);
    }

    /// <summary>DELETE /api/v1/projects/{projectId}/share/{shareId} — revocar link</summary>
    [HttpDelete("{shareId:guid}")]
    public async Task<IActionResult> Revoke(Guid projectId, Guid shareId, CancellationToken ct = default)
    {
        await _mediator.Send(new RevokeProjectShareCommand(shareId), ct);
        return NoContent();
    }

    public record CreateShareRequest(
        string?   Message,
        DateTime? ExpiresAt,
        string[]? IncludedFileIds,
        string[]? IncludedDeliverableIds);
}

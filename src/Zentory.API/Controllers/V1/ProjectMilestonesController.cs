using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/milestones")]
public sealed class ProjectMilestonesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectMilestonesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects/{projectId}/milestones</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectMilestonesQuery(projectId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects/{projectId}/milestones — crear hito</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId, [FromBody] CreateProjectMilestoneCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command with { ProjectId = projectId }, ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/projects/{projectId}/milestones/{milestoneId}/status — iniciar/completar/reabrir</summary>
    [HttpPatch("{milestoneId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        Guid projectId, Guid milestoneId, [FromBody] ChangeProjectMilestoneStatusCommand command,
        CancellationToken ct = default)
    {
        await _mediator.Send(command with { ProjectId = projectId, MilestoneId = milestoneId }, ct);
        return NoContent();
    }
}

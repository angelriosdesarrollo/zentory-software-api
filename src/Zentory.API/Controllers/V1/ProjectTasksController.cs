using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.ProjectTasks.Commands;
using Zentory.Application.ProjectTasks.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/tasks")]
public sealed class ProjectTasksController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectTasksController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects/{projectId}/tasks</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectTasksQuery(projectId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects/{projectId}/tasks</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId,
        [FromBody] CreateProjectTaskCommand command,
        CancellationToken ct = default)
    {
        var id = await _mediator.Send(command with { ProjectId = projectId }, ct);
        return CreatedAtAction(nameof(List), new { projectId }, new { id });
    }

    /// <summary>PATCH /api/v1/projects/{projectId}/tasks/{taskId}</summary>
    [HttpPatch("{taskId:guid}")]
    public async Task<IActionResult> Update(
        Guid projectId,
        Guid taskId,
        [FromBody] UpdateProjectTaskCommand command,
        CancellationToken ct = default)
    {
        await _mediator.Send(command with { TaskId = taskId }, ct);
        return NoContent();
    }

    /// <summary>PATCH /api/v1/projects/{projectId}/tasks/{taskId}/move</summary>
    [HttpPatch("{taskId:guid}/move")]
    public async Task<IActionResult> Move(
        Guid projectId,
        Guid taskId,
        [FromBody] MoveRequest body,
        CancellationToken ct = default)
    {
        await _mediator.Send(new MoveProjectTaskCommand(taskId, body.Status), ct);
        return NoContent();
    }

    /// <summary>PATCH /api/v1/projects/{projectId}/tasks/{taskId}/gantt</summary>
    [HttpPatch("{taskId:guid}/gantt")]
    public async Task<IActionResult> UpdateGantt(
        Guid projectId,
        Guid taskId,
        [FromBody] GanttRequest body,
        CancellationToken ct = default)
    {
        await _mediator.Send(new UpdateProjectTaskGanttCommand(
            taskId,
            body.MilestoneId,
            body.StartDate,
            body.DueDate,
            body.Hours,
            body.Dependencies ?? []), ct);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/projects/{projectId}/tasks/{taskId}</summary>
    [HttpDelete("{taskId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteProjectTaskCommand(taskId), ct);
        return NoContent();
    }

    public record MoveRequest(string Status);
    public record GanttRequest(Guid? MilestoneId, string? StartDate, string? DueDate, int Hours, string[]? Dependencies);
}

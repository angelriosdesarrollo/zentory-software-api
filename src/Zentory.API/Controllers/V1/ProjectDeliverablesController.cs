using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Projects.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/deliverables")]
public sealed class ProjectDeliverablesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectDeliverablesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects/{projectId}/deliverables</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectDeliverablesQuery(projectId), ct);
        return Ok(result);
    }
}

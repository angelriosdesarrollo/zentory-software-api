using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.ActivityLogs.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/activity-log")]
public sealed class ActivityLogController : ControllerBase
{
    private readonly IMediator _mediator;

    public ActivityLogController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/activity-log?entityType=Proposal&amp;from=2026-01-01&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string?   entityType = null,
        [FromQuery] Guid?     entityId   = null,
        [FromQuery] DateTime? from       = null,
        [FromQuery] DateTime? to         = null,
        [FromQuery] int       page       = 1,
        [FromQuery] int       pageSize   = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetActivityLogQuery(entityType, entityId, from, to, page, pageSize), ct);
        return Ok(result);
    }
}

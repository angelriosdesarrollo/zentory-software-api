using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Proposals.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/proposals/{proposalId:guid}/activity-log")]
public sealed class ProposalActivityLogController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProposalActivityLogController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/proposals/{proposalId}/activity-log</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid proposalId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProposalActivityLogQuery(proposalId), ct);
        return Ok(result);
    }
}

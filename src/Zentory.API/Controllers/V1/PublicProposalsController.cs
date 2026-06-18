using MediatR;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Proposals.Commands;
using Zentory.Application.Proposals.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Route("api/v1/public/proposals")]
public sealed class PublicProposalsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicProposalsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/public/proposals/{token} — propuesta pública por token</summary>
    [HttpGet("{token:guid}")]
    public async Task<IActionResult> GetByToken(Guid token, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProposalByTokenQuery(token), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/public/proposals/{token}/view — registrar apertura</summary>
    [HttpPost("{token:guid}/view")]
    public async Task<IActionResult> RecordView(Guid token, CancellationToken ct = default)
    {
        await _mediator.Send(new RecordProposalViewCommand(token), ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/public/proposals/{token}/accept — aceptar propuesta</summary>
    [HttpPost("{token:guid}/accept")]
    public async Task<IActionResult> Accept(Guid token, CancellationToken ct = default)
    {
        await _mediator.Send(new AcceptProposalByTokenCommand(token), ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/public/proposals/{token}/reject — rechazar propuesta</summary>
    [HttpPost("{token:guid}/reject")]
    public async Task<IActionResult> Reject(Guid token, CancellationToken ct = default)
    {
        await _mediator.Send(new RejectProposalByTokenCommand(token), ct);
        return NoContent();
    }
}

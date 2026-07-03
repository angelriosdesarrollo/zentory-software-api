using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.PilaVerifications.Commands;
using Zentory.Application.PilaVerifications.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize(Policy = "EmpresaStudio")]
[Route("api/v1/team/pila-verifications")]
public sealed class PilaVerificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PilaVerificationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/team/pila-verifications?period=YYYY-MM</summary>
    [HttpGet]
    public async Task<IActionResult> GetCompliance([FromQuery] string period, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPilaComplianceQuery(period), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/team/pila-verifications/request</summary>
    [HttpPost("request")]
    public async Task<IActionResult> RequestVerification([FromBody] RequestPilaVerificationCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/team/pila-verifications/{collaboratorId}/history</summary>
    [HttpGet("{collaboratorId:guid}/history")]
    public async Task<IActionResult> GetHistory(Guid collaboratorId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPilaHistoryQuery(collaboratorId), ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/team/pila-verifications/{id}/verify</summary>
    [HttpPatch("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, [FromBody] VerifyPilaCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    /// <summary>GET /api/v1/team/pila-verifications/{id}/download-url</summary>
    [HttpGet("{id:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid id, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetPilaDownloadUrlQuery(id), ct);
        return Ok(new { url });
    }

    /// <summary>GET /api/v1/team/pila-verifications/{id}/documents — historial de versiones subidas</summary>
    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> GetDocuments(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPilaVerificationDocumentsQuery(id), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/team/pila-verifications/documents/{documentId}/download-url</summary>
    [HttpGet("documents/{documentId:guid}/download-url")]
    public async Task<IActionResult> GetDocumentDownloadUrl(Guid documentId, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetPilaVerificationDocumentDownloadUrlQuery(documentId), ct);
        return Ok(new { url });
    }
}

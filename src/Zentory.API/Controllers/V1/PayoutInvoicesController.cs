using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.PayoutInvoices.Commands;
using Zentory.Application.PayoutInvoices.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize(Policy = "EmpresaStudio")]
[Route("api/v1/team/payout-invoices")]
public sealed class PayoutInvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayoutInvoicesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/team/payout-invoices?collaboratorId=</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid collaboratorId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPayoutInvoicesQuery(collaboratorId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/team/payout-invoices/generate</summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GeneratePayoutInvoiceCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(new { id });
    }

    /// <summary>POST /api/v1/team/payout-invoices/{id}/send</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new SendPayoutInvoiceCommand(id), ct);
        return NoContent();
    }

    /// <summary>GET /api/v1/team/payout-invoices/{id}/download-url</summary>
    [HttpGet("{id:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid id, CancellationToken ct = default)
    {
        var url = await _mediator.Send(new GetPayoutInvoiceDownloadUrlQuery(id), ct);
        return Ok(new { url });
    }

    /// <summary>POST /api/v1/team/payout-invoices/request-manual-upload</summary>
    [HttpPost("request-manual-upload")]
    public async Task<IActionResult> RequestManualUpload(
        [FromBody] RequestManualPayoutInvoiceCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return Ok(new { id });
    }

    /// <summary>GET /api/v1/team/payout-invoices/suggested-amount?collaboratorId=&amp;period=YYYY-MM</summary>
    [HttpGet("suggested-amount")]
    public async Task<IActionResult> GetSuggestedAmount(
        [FromQuery] Guid collaboratorId, [FromQuery] string period, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSuggestedPayoutAmountQuery(collaboratorId, period), ct);
        return Ok(result);
    }
}

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Invoices.Commands;
using Zentory.Application.Invoices.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/invoices</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status   = null,
        [FromQuery] Guid?   clientId = null,
        CancellationToken   ct       = default)
    {
        var result = await _mediator.Send(new GetInvoicesQuery(status, clientId), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/invoices/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetInvoiceByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/invoices — crear factura con ítems</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceCommand command, CancellationToken ct = default)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>POST /api/v1/invoices/{id}/send — marcar como enviada</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new SendInvoiceCommand(id), ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/invoices/{id}/payments — registrar pago</summary>
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] RecordPaymentCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command with { Id = id }, ct);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/invoices/{id} — soft delete</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteInvoiceCommand(id), ct);
        return NoContent();
    }
}

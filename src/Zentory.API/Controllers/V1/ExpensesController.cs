using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;

namespace Zentory.API.Controllers.V1;

// Separado de ProjectExpensesController (anidado bajo /projects/{projectId}) porque aquí el
// propósito es poder reasignar el projectId de un gasto ya existente, incluyendo dejarlo como
// gasto de empresa (ProjectId = null).
[ApiController]
[Authorize]
[Route("api/v1/expenses")]
public sealed class ExpensesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ExpensesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/expenses — todos los gastos (cualquier proyecto + de empresa), para /finances</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAllExpensesQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/v1/expenses — crear un gasto sin depender de la ruta anidada de un proyecto,
    /// para poder elegir "gasto de empresa" (ProjectId = null) o cualquier proyecto desde el
    /// modal de registro manual, no solo el proyecto que estaba abierto.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGlobalExpenseRequest body, CancellationToken ct = default)
    {
        var expense = await _mediator.Send(new CreateProjectExpenseCommand(
            ProjectId:   body.ProjectId,
            Date:        body.Date,
            Category:    body.Category,
            Description: body.Description,
            Amount:      body.Amount,
            Currency:    body.Currency,
            Vendor:      body.Vendor,
            Billable:    body.Billable), ct);

        return Ok(expense);
    }

    public record CreateGlobalExpenseRequest(
        Guid?   ProjectId,
        string  Date,
        string  Category,
        string  Description,
        decimal Amount,
        string  Currency,
        string? Vendor,
        bool    Billable);

    /// <summary>PATCH /api/v1/expenses/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateExpenseRequest body, CancellationToken ct = default)
    {
        var expense = await _mediator.Send(new UpdateProjectExpenseCommand(
            Id:          id,
            ProjectId:   body.ProjectId,
            Date:        body.Date,
            Category:    body.Category,
            Description: body.Description,
            Amount:      body.Amount,
            Currency:    body.Currency,
            Vendor:      body.Vendor,
            Billable:    body.Billable,
            Status:      body.Status), ct);

        return Ok(expense);
    }

    public record UpdateExpenseRequest(
        Guid?   ProjectId,
        string  Date,
        string  Category,
        string  Description,
        decimal Amount,
        string  Currency,
        string? Vendor,
        bool    Billable,
        string  Status);
}

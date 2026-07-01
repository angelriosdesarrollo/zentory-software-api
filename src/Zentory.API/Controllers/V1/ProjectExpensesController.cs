using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Projects.Commands;
using Zentory.Application.Projects.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/expenses")]
public sealed class ProjectExpensesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProjectExpensesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/projects/{projectId}/expenses</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProjectExpensesQuery(projectId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/projects/{projectId}/expenses</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId,
        [FromBody] CreateExpenseRequest body,
        CancellationToken ct = default)
    {
        var expense = await _mediator.Send(new CreateProjectExpenseCommand(
            ProjectId:   projectId,
            Date:        body.Date,
            Category:    body.Category,
            Description: body.Description,
            Amount:      body.Amount,
            Currency:    body.Currency,
            Vendor:      body.Vendor,
            Billable:    body.Billable), ct);

        return CreatedAtAction(nameof(List), new { projectId }, expense);
    }

    public record CreateExpenseRequest(
        string  Date,
        string  Category,
        string  Description,
        decimal Amount,
        string  Currency,
        string? Vendor,
        bool    Billable);
}

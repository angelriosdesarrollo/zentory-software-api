using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Organization.Commands;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/organizations")]
public sealed class OrganizationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/organizations — crear una nueva organización para el usuario autenticado (queda como owner)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}

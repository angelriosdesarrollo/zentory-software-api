using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Auth.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProfileController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/me — perfil del usuario autenticado</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProfileQuery(), ct);
        return Ok(result);
    }
}

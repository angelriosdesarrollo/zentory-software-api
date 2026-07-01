using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Auth.Queries;
using Zentory.Application.Auth.DTOs;

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

    /// <summary>GET /api/v1/me/organizations — organizaciones a las que pertenece el usuario</summary>
    [HttpGet("organizations")]
    public async Task<IActionResult> GetOrganizations(CancellationToken ct = default)
    {
        IReadOnlyList<OrgMembershipDto> result = await _mediator.Send(new ListUserOrganizationsQuery(), ct);
        return Ok(result);
    }
}

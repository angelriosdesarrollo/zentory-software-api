using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Auth.Commands;

namespace Zentory.API.Controllers.V1;

[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/auth/login — autenticar usuario</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/register — registrar nueva organización y usuario owner</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(201, result);
    }

    /// <summary>POST /api/v1/auth/refresh — renovar tokens (rotation)</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/google — login/registro con id_token de Google verificado por el backend</summary>
    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleLoginCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }
}

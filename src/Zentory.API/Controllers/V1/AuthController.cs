using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Exceptions;
using Zentory.API.Authorization;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Controllers.V1;

// [AllowAnonymous] va por acción y no en la clase (PLAN-0018 RF-60, D-17): en la clase anularía el [Authorize] de
// resend-verification y reauth-nonce. Cada endpoint de RF-29 lleva su [AuthRateLimit], que corre después del model binding
// (la clave de login y forgot-password usa el email normalizado del cuerpo) y antes del comando: un 429 RATE_LIMITED no
// emite tokens, ni encola correos, ni crea cuentas. refresh y logout no tienen límite propio en RF-29.
[ApiController]
[SkipMembershipValidation] // lista cerrada de PLAN-0018 RF-14 (SkipMembershipCatalogTests)
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/auth/login — autenticar usuario</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [AuthRateLimit(AuthRateLimitPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/register — registrar nueva organización y usuario owner</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [AuthRateLimit(AuthRateLimitPolicies.Register)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(201, result);
    }

    /// <summary>POST /api/v1/auth/refresh — renovar tokens (rotation)</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/logout — revocar el refresh token presentado</summary>
    // PLAN-0018 RF-30 d: anónima como refresh (el access token puede haber expirado) y siempre 204, también con un token
    // inexistente o ya revocado, para no revelar si existía.
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutCommand? command, CancellationToken ct = default)
    {
        await _mediator.Send(command ?? new LogoutCommand(null), ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/auth/google — login/registro con id_token de Google verificado por el backend</summary>
    [AllowAnonymous]
    [HttpPost("google")]
    [AuthRateLimit(AuthRateLimitPolicies.Google)]
    public async Task<IActionResult> Google([FromBody] GoogleLoginCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/forgot-password — pedir el enlace de reset de contraseña</summary>
    // PLAN-0018 RF-21 a, RF-23: 200 con el mismo cuerpo exista o no la cuenta; el correo sale por la cola, fuera del request.
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [AuthRateLimit(AuthRateLimitPolicies.ForgotPassword)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/reset-password — canjear el enlace y fijar la contraseña nueva</summary>
    // PLAN-0018 RF-21 b, RF-24: 204; token inexistente, usado o vencido → 422 TOKEN_INVALID.
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [AuthRateLimit(AuthRateLimitPolicies.ResetPassword)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/auth/verify-email — canjear el enlace de verificación del correo</summary>
    // PLAN-0018 RF-21 c, RF-22 c: 204; token inexistente, usado, vencido o de otro email → 422 TOKEN_INVALID.
    [AllowAnonymous]
    [HttpPost("verify-email")]
    [AuthRateLimit(AuthRateLimitPolicies.VerifyEmail)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailCommand command, CancellationToken ct = default)
    {
        await _mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/auth/invitations/accept — aceptar una invitación a una organización</summary>
    // PLAN-0018 RF-50, RF-60: anónima, y usa el usuario del Bearer si es válido (la autenticación por defecto ya lo leyó en
    // HttpContext.User; un Bearer inválido o vencido cuenta como sin JWT). Sin JWT crea la cuenta e inicia sesión; con JWT
    // conserva la sesión del token. 200 AuthTokenDto con la organización invitada activa. Como acción [AllowAnonymous], el
    // middleware no compara el sid (C-16): con JWT lo compara el comando (T29c).
    [AllowAnonymous]
    [HttpPost("invitations/accept")]
    [AuthRateLimit(AuthRateLimitPolicies.AcceptInvitation)]
    public async Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptInvitationCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/auth/resend-verification — reenviar el enlace de verificación del usuario autenticado</summary>
    // PLAN-0018 RF-21 d, RF-60: exige el JWT de usuario y el usuario sale del sub. 202 siempre; sin efecto si la cuenta ya
    // está verificada. Excluida de la revalidación de membresía como el resto del controlador, pero el middleware sí
    // compara la sesión (RF-90 d): un access token de una sesión reemplazada recibe 401 SESSION_REPLACED.
    [Authorize]
    [HttpPost("resend-verification")]
    [AuthRateLimit(AuthRateLimitPolicies.ResendVerification)]
    public async Task<IActionResult> ResendVerification(
        [FromServices] ITenantContext tenant, CancellationToken ct = default)
    {
        await _mediator.Send(new ResendVerificationCommand(tenant.UserId), ct);
        return Accepted();
    }

    /// <summary>GET /api/v1/auth/reauth-nonce?action= — nonce de un solo uso para reautenticarse con Google</summary>
    // PLAN-0018 RF-84: emisión sin estado, ligada al sub del JWT y a la acción, 5 min. Excluida de la revalidación de
    // membresía como el resto del controlador: sirve también a la sesión sin organización activa (aceptar una
    // transferencia). La acción desconocida responde 422 sin eco del valor.
    [Authorize]
    [HttpGet("reauth-nonce")]
    public IActionResult GetReauthNonce(
        [FromQuery] string? action,
        [FromServices] ReauthNonce nonces,
        [FromServices] ITenantContext tenant)
    {
        if (!ReauthAction.IsKnown(action))
            throw new ValidationException([new ValidationError("action", "Acción de reautenticación desconocida.")]);

        var issued = nonces.Issue(tenant.UserId, action!);

        // El nonce no se cachea en intermediarios ni en el navegador.
        Response.Headers.CacheControl = "no-store";
        return Ok(new { nonce = issued.Nonce, expiresAt = issued.ExpiresAt });
    }
}

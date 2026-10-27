using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zentory.Application.Organization.Commands;
using Zentory.API.Authorization;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Controllers.V1;

// Acciones del destino de una transferencia de la propiedad (PLAN-0018 RF-83 b, f; D-33; T30c). Son de usuario y no de
// la organización activa: [SkipMembershipValidation] (lista cerrada de RF-14) y cada comando comprueba que la propuesta
// es del que llama (ToUserId == sub), con el mismo 404 en cualquier otro caso. La clase lleva [Authorize]: el middleware
// compara la sesión (sid) también en estas acciones (RF-90 d).
[ApiController]
[Authorize]
[Route("api/v1/ownership-transfers")]
public sealed class OwnershipTransfersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OwnershipTransfersController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/ownership-transfers/{id}/accept — el destino acepta la transferencia con su credencial</summary>
    // Orden de RF-83 b: 404 uniforme (filtro, sin contar intento) → reauth:{sub} (5/15 min, RF-81 f) → reautenticación del
    // destino → revalidación → efecto. El cuerpo no se registra (SR-0018.30 e).
    [SkipMembershipValidation]
    [OwnershipTransferTarget]
    [AuthRateLimit(AuthRateLimitPolicies.Reauth)]
    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(
        Guid id,
        [FromBody] AcceptOwnershipTransferRequest? body,
        CancellationToken ct = default)
    {
        await _mediator.Send(new AcceptOwnershipTransferCommand(id, body?.CurrentPassword, body?.GoogleIdToken), ct);
        return NoContent();
    }

    // Sin ToString con valores: el record imprimiría la credencial.
    public sealed record AcceptOwnershipTransferRequest(string? CurrentPassword, string? GoogleIdToken)
    {
        public override string ToString() => nameof(AcceptOwnershipTransferRequest);
    }

    /// <summary>POST /api/v1/ownership-transfers/{id}/decline — el destino rechaza la transferencia</summary>
    // RF-83 f: sin reautenticación (RF-81) ni rate limit propio; 204, o el 404 uniforme de aceptar.
    [SkipMembershipValidation]
    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new DeclineOwnershipTransferCommand(id), ct);
        return NoContent();
    }

    // Paso 1 de RF-83 b antes del rate limit: corre antes que [AuthRateLimit] (Order menor), así quien no es el destino
    // recibe el 404 uniforme aunque haya agotado reauth:{sub}, y ese 404 no cuenta como intento con credencial.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OwnershipTransferTargetAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
    {
        public const string IdArgument = "id";

        public int Order => -1;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var id = context.ActionArguments.TryGetValue(IdArgument, out var value) && value is Guid guid ? guid : Guid.Empty;
            var mediator = context.HttpContext.RequestServices.GetRequiredService<IMediator>();
            await mediator.Send(new EnsureOwnershipTransferTargetCommand(id), context.HttpContext.RequestAborted);
            await next();
        }
    }
}

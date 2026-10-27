using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Billing.Queries;
using Zentory.Application.Organization.Commands;
using Zentory.Application.Organization.Queries;
using Zentory.API.Authorization;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/organization")]
public sealed class OrganizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/organization/profile — perfil fiscal de la organización</summary>
    [AnyOrgMember]
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrganizationProfileQuery(), ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/organization/profile — actualizar perfil fiscal</summary>
    // Owner y admin; member → 403 ACCESS_DENIED (PLAN-0018 RF-15). La lectura sigue abierta a cualquier miembro vigente.
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
    [HttpPatch("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateOrganizationProfileCommand command,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/settings — configuraciones clave-valor del tenant</summary>
    [AnyOrgMember]
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrganizationSettingsQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// PATCH /api/v1/organization/settings — actualizar uno o varios settings.
    /// El body es el diccionario plano tal como lo manda el frontend
    /// (lib/api/settings.ts::updateOrganizationSettings), no un objeto envolvente
    /// — de ahí el binding manual en vez de bindear el record de MediatR directo.
    /// </summary>
    [Authorize(Policy = PolicyNames.RequireOrgAdmin)]
    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] Dictionary<string, string?> settings,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new UpdateOrganizationSettingsCommand(settings), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/plan — plan activo y fecha de renovación</summary>
    // Solo el owner (PLAN-0018 RF-66 d, RF-59 d; lista cerrada de RF-87 a): lleva PriceMonthlyUsd y el plan es del owner.
    // Admin y member: 403 ACCESS_DENIED.
    [Authorize(Policy = PolicyNames.RequireOrgOwner)]
    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrgPlanQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/billing-history — historial de pagos de la suscripción</summary>
    // Solo el owner (PLAN-0018 RF-66 d; lista cerrada de RF-87 a): son los montos de la suscripción. Admin y member: 403
    // ACCESS_DENIED.
    [Authorize(Policy = PolicyNames.RequireOrgOwner)]
    [HttpGet("billing-history")]
    public async Task<IActionResult> GetBillingHistory(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetBillingHistoryQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/plan-limits — límites cuantitativos del plan actual</summary>
    [AnyOrgMember]
    [HttpGet("plan-limits")]
    public async Task<IActionResult> GetPlanLimits(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPlanLimitsQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/organization/switch — cambiar organización activa; devuelve nuevo access token</summary>
    // Excluida (RF-14): SwitchActiveOrganizationCommand valida la membresía de destino y responde 404 sin ella (RF-16).
    [SkipMembershipValidation]
    [HttpPost("switch")]
    public async Task<IActionResult> Switch([FromBody] SwitchRequest body, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SwitchActiveOrganizationCommand(body.OrgId), ct);
        return Ok(result);
    }

    public record SwitchRequest(Guid OrgId);

    /// <summary>DELETE /api/v1/organization — eliminar la organización activa (borrado lógico, restaurable)</summary>
    // Orden de RF-81 e: RequireOrgOwner (admin y member: 403 ACCESS_DENIED sin llegar a la credencial) → organization-delete
    // (5/h, SR-0018.29) y reauth:{sub} (5/15 min, RF-81 f) → reautenticación → confirmName → efecto. El cuerpo no se
    // registra (SR-0018.30 e).
    [Authorize(Policy = PolicyNames.RequireOrgOwner)]
    [AuthRateLimit(AuthRateLimitPolicies.OrganizationDelete)]
    [AuthRateLimit(AuthRateLimitPolicies.Reauth)]
    [HttpDelete]
    public async Task<IActionResult> Delete([FromBody] DeleteOrganizationRequest body, CancellationToken ct = default)
    {
        await _mediator.Send(new DeleteOrganizationCommand(body.ConfirmName, body.CurrentPassword, body.GoogleIdToken), ct);
        return NoContent();
    }

    // Sin ToString con valores: el record imprimiría la credencial.
    public sealed record DeleteOrganizationRequest(string? ConfirmName, string? CurrentPassword, string? GoogleIdToken)
    {
        public override string ToString() => nameof(DeleteOrganizationRequest);
    }

    /// <summary>GET /api/v1/organization/transfer-ownership/preview?userId= — aviso previo de la transferencia</summary>
    // RF-77 (SR-0018.46): solo el owner; sin reautenticación ni cambios. 404 si userId no es miembro vigente, 422 hacia uno
    // mismo, 403 EMAIL_NOT_VERIFIED.
    [Authorize(Policy = PolicyNames.RequireOrgOwner)]
    [HttpGet("transfer-ownership/preview")]
    public async Task<IActionResult> GetTransferOwnershipPreview([FromQuery] Guid userId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetTransferOwnershipPreviewQuery(userId), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/organization/transfer-ownership — proponer la transferencia de la propiedad</summary>
    // RF-64 (D-33): 202 { transferId, expiresAt }; nada cambia hasta que el destino acepte (T30c). Orden de RF-81 e:
    // RequireOrgOwner → organization-transfer (5/h, SR-0018.29) y reauth:{sub} (5/15 min, RF-81 f) → reautenticación →
    // destino, límite y acuse → una sola pendiente. El cuerpo no se registra (SR-0018.30 e).
    [Authorize(Policy = PolicyNames.RequireOrgOwner)]
    [AuthRateLimit(AuthRateLimitPolicies.OrganizationTransfer)]
    [AuthRateLimit(AuthRateLimitPolicies.Reauth)]
    [HttpPost("transfer-ownership")]
    public async Task<IActionResult> ProposeOwnershipTransfer(
        [FromBody] ProposeOwnershipTransferRequest body,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ProposeOwnershipTransferCommand(
            body.UserId, body.AcknowledgedPreviewHash, body.CurrentPassword, body.GoogleIdToken), ct);
        return StatusCode(StatusCodes.Status202Accepted, result);
    }

    // Sin ToString con valores: el record imprimiría la credencial.
    public sealed record ProposeOwnershipTransferRequest(
        Guid    UserId,
        string? AcknowledgedPreviewHash,
        string? CurrentPassword,
        string? GoogleIdToken)
    {
        public override string ToString() => nameof(ProposeOwnershipTransferRequest);
    }

    /// <summary>DELETE /api/v1/organization/transfer-ownership — cancelar la propuesta pendiente</summary>
    // RF-64 c3: solo el owner, sin reautenticación; idempotente (204 también sin propuesta pendiente).
    [Authorize(Policy = PolicyNames.RequireOrgOwner)]
    [HttpDelete("transfer-ownership")]
    public async Task<IActionResult> CancelOwnershipTransfer(CancellationToken ct = default)
    {
        await _mediator.Send(new CancelOwnershipTransferCommand(), ct);
        return NoContent();
    }
}

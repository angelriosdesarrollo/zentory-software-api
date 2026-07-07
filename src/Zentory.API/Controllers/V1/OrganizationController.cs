using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Billing.Queries;
using Zentory.Application.Organization.Commands;
using Zentory.Application.Organization.Queries;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/organization")]
public sealed class OrganizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/organization/profile — perfil fiscal de la organización</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrganizationProfileQuery(), ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/organization/profile — actualizar perfil fiscal</summary>
    [HttpPatch("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateOrganizationProfileCommand command,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/settings — configuraciones clave-valor del tenant</summary>
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
    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] Dictionary<string, string?> settings,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new UpdateOrganizationSettingsCommand(settings), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/members — miembros del equipo</summary>
    [HttpGet("members")]
    public async Task<IActionResult> GetMembers(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrganizationMembersQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/plan — plan activo y fecha de renovación</summary>
    [HttpGet("plan")]
    public async Task<IActionResult> GetPlan(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrgPlanQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/billing-history — historial de pagos de la suscripción</summary>
    [HttpGet("billing-history")]
    public async Task<IActionResult> GetBillingHistory(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetBillingHistoryQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/plan-limits — límites cuantitativos del plan actual</summary>
    [HttpGet("plan-limits")]
    public async Task<IActionResult> GetPlanLimits(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPlanLimitsQuery(), ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/organization/switch — cambiar organización activa; devuelve nuevo access token</summary>
    [HttpPost("switch")]
    public async Task<IActionResult> Switch([FromBody] SwitchRequest body, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SwitchActiveOrganizationCommand(body.OrgId), ct);
        return Ok(result);
    }

    public record SwitchRequest(Guid OrgId);
}

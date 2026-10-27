using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Organization.Commands;
using Zentory.Application.Organization.Queries;
using Zentory.API.Authorization;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Controllers.V1;

// Gestión de miembros de la organización activa (PLAN-0018 RF-46, RF-47, RF-48, RF-49; D-19). Toda la clase usa ManageMembers:
// empresa + plan efectivo pro + owner o admin. GET /organization/members sale de OrganizationController con la misma
// ruta y el mismo DTO (lo consume el frontend).
[ApiController]
[Authorize(Policy = PolicyNames.ManageMembers)]
[Route("api/v1/organization/members")]
public sealed class OrganizationMembersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationMembersController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/organization/members — miembros vigentes de la organización activa</summary>
    [HttpGet]
    public async Task<IActionResult> GetMembers(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrganizationMembersQuery(), ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/v1/organization/members/{userId}/role — cambia el rol de otro miembro (admin o member)</summary>
    [HttpPatch("{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(
        Guid userId, [FromBody] ChangeMemberRoleRequest body, CancellationToken ct = default)
    {
        await _mediator.Send(new ChangeMemberRoleCommand(userId, body.RoleId), ct);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/organization/members/{userId} — da de baja a otro miembro (borrado lógico)</summary>
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken ct = default)
    {
        await _mediator.Send(new RemoveMemberCommand(userId), ct);
        return NoContent();
    }

    /// <summary>POST /api/v1/organization/members/invite — invita a un email con rol admin o member (RF-49)</summary>
    // El techo por organización (SR-0018.22) corre después de ManageMembers y antes del comando: un 429 no crea
    // invitación ni encola correo. El techo por email destino lo consume el comando tras sus validaciones.
    [HttpPost("invite")]
    [AuthRateLimit(AuthRateLimitPolicies.OrganizationInvite)]
    public async Task<IActionResult> Invite([FromBody] InviteMemberRequest body, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new InviteMemberCommand(body.Email ?? string.Empty, body.RoleId, body.Name), ct);
        return Accepted(new { invitationId = result.InvitationId, expiresAt = result.ExpiresAt });
    }

    public sealed record ChangeMemberRoleRequest(string? RoleId);

    // name? solo se usa en el saludo del correo y no se persiste (RF-49 b).
    public sealed record InviteMemberRequest(string? Email, string? RoleId, string? Name);
}

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Organization.Queries;
using Zentory.API.Extensions;

namespace Zentory.API.Controllers.V1;

// Catálogo fijo de roles y su matriz de permisos (PLAN-0018 RF-45, RF-47; D-18, D-19). Mismo gating que la gestión de
// miembros: ManageMembers en toda la clase.
[ApiController]
[Authorize(Policy = PolicyNames.ManageMembers)]
[Route("api/v1/organization/roles")]
public sealed class OrganizationRolesController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationRolesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/organization/roles — owner, admin y member</summary>
    [HttpGet]
    public async Task<IActionResult> GetRoles(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetOrgRolesQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/organization/roles/permissions — matriz de solo lectura de RF-45 b</summary>
    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetRolePermissionsQuery(), ct);
        return Ok(result);
    }
}

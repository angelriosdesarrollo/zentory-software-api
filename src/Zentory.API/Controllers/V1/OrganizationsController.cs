using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Organization.Commands;
using Zentory.API.Authorization;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/organizations")]
public sealed class OrganizationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>POST /api/v1/organizations — crear una nueva organización para el usuario autenticado (queda como owner)</summary>
    // Excluida (RF-14, RF-85): crea una organización propia del usuario; no actúa sobre la organización activa (D-30).
    [SkipMembershipValidation]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationCommand command, CancellationToken ct = default)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    /// <summary>POST /api/v1/organizations/{id}/restore — restaurar una organización propia eliminada dentro del plazo</summary>
    // Excluida (RF-14, RF-85): el owner restaura desde cualquier sesión, también sin organización activa; el comando valida
    // la propiedad por sí mismo (404 uniforme, SR-0018.25). organization-restore: 5 por hora por usuario (SR-0018.29).
    // Sin reautenticación: restaurar no destruye nada (RF-72).
    [SkipMembershipValidation]
    [AuthRateLimit(AuthRateLimitPolicies.OrganizationRestore)]
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct = default)
    {
        await _mediator.Send(new RestoreOrganizationCommand(id), ct);
        return NoContent();
    }
}

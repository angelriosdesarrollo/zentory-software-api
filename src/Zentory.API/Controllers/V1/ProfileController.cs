using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zentory.Application.Auth.Queries;
using Zentory.Application.Auth.DTOs;
using Zentory.Application.Organization.Queries;
using Zentory.API.Authorization;

namespace Zentory.API.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProfileController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/me — perfil del usuario autenticado</summary>
    // Excluida (RF-14, RF-85): sirve a la sesión sin organización activa.
    [SkipMembershipValidation]
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetProfileQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/me/organizations — organizaciones a las que pertenece el usuario</summary>
    // Excluida (RF-14): quien perdió la membresía de su organización activa elige otra desde aquí.
    [SkipMembershipValidation]
    [HttpGet("organizations")]
    public async Task<IActionResult> GetOrganizations(CancellationToken ct = default)
    {
        IReadOnlyList<OrgMembershipDto> result = await _mediator.Send(new ListUserOrganizationsQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/me/organizations/deleted — organizaciones propias eliminadas que aún se pueden restaurar</summary>
    // Excluida (RF-14, RF-85): la usa la sesión sin organización activa; lista solo las de OwnerId == sub (SR-0018.25).
    [SkipMembershipValidation]
    [HttpGet("organizations/deleted")]
    public async Task<IActionResult> GetDeletedOrganizations(CancellationToken ct = default)
    {
        IReadOnlyList<DeletedOrganizationDto> result = await _mediator.Send(new ListDeletedOrganizationsQuery(), ct);
        return Ok(result);
    }

    /// <summary>GET /api/v1/me/ownership-transfers — propuestas de transferencia pendientes cuyo destino es el usuario</summary>
    // Excluida (RF-14, RF-83 a): el destino la consulta con cualquier organización activa; lista solo las de ToUserId ==
    // sub, pending, vigentes y de organizaciones vigentes. Sin reautenticación (RF-81).
    [SkipMembershipValidation]
    [HttpGet("ownership-transfers")]
    public async Task<IActionResult> GetOwnershipTransfers(CancellationToken ct = default)
    {
        IReadOnlyList<MyOwnershipTransferDto> result = await _mediator.Send(new ListMyOwnershipTransfersQuery(), ct);
        return Ok(result);
    }
}

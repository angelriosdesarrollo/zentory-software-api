using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Exceptions;
using Zentory.Application.Organization.Services;

namespace Zentory.Application.Organization.Queries;

// GET /organization/transfer-ownership/preview?userId= (PLAN-0018 RF-77; SR-0018.46; D-33): aviso previo del owner antes
// de proponer la transferencia. La policy RequireOrgOwner ya corrió en el controlador (admin y member: 403). Sin
// reautenticación (RF-81) y sin cambios: 404 si el destino no es miembro vigente, 422 hacia uno mismo y 403
// EMAIL_NOT_VERIFIED, igual que al proponer.
public sealed record GetTransferOwnershipPreviewQuery(Guid UserId) : IRequest<OwnershipTransferPreviewDto>;

public sealed class GetTransferOwnershipPreviewQueryHandler
    : IRequestHandler<GetTransferOwnershipPreviewQuery, OwnershipTransferPreviewDto>
{
    private readonly IZentoryDbContext      _db;
    private readonly ITenantContext         _tenant;
    private readonly IPlanResolutionService _plans;
    private readonly IPlanLimitService      _planLimits;
    private readonly TimeProvider           _time;

    public GetTransferOwnershipPreviewQueryHandler(
        IZentoryDbContext      db,
        ITenantContext         tenant,
        IPlanResolutionService plans,
        IPlanLimitService      planLimits,
        TimeProvider           time)
    {
        _db         = db;
        _tenant     = tenant;
        _plans      = plans;
        _planLimits = planLimits;
        _time       = time;
    }

    public async Task<OwnershipTransferPreviewDto> Handle(GetTransferOwnershipPreviewQuery request, CancellationToken ct)
    {
        var organizationId = _tenant.OrganizationId;
        var organization = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException("Organization", organizationId);

        await OwnershipTransferPreview.ResolveTargetAsync(_db, organizationId, _tenant.UserId, request.UserId, ct);

        return await OwnershipTransferPreview.BuildAsync(
            _db, _plans, _planLimits, organization, request.UserId, _time.GetUtcNow().UtcDateTime, ct);
    }
}

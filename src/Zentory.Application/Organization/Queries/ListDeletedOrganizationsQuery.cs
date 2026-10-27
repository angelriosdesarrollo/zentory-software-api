using MediatR;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.Services;

namespace Zentory.Application.Organization.Queries;

// GET /me/organizations/deleted (PLAN-0018 RF-72 a): las organizaciones propias del que llama que siguen restaurables.
// Solo por OwnerId == sub: un admin o member de la organización eliminada no la ve (SR-0018.25). No usa la organización
// activa, así sirve también a la sesión sin organización (RF-85).
public sealed record ListDeletedOrganizationsQuery : IRequest<IReadOnlyList<DeletedOrganizationDto>>;

public sealed record DeletedOrganizationDto(Guid Id, string Name, DateTime DeletedAt, DateTime PurgeAt);

public sealed class ListDeletedOrganizationsQueryHandler
    : IRequestHandler<ListDeletedOrganizationsQuery, IReadOnlyList<DeletedOrganizationDto>>
{
    private readonly IDeletedOrganizationReader _deleted;
    private readonly ITenantContext             _tenant;

    public ListDeletedOrganizationsQueryHandler(IDeletedOrganizationReader deleted, ITenantContext tenant)
    {
        _deleted = deleted;
        _tenant  = tenant;
    }

    public async Task<IReadOnlyList<DeletedOrganizationDto>> Handle(ListDeletedOrganizationsQuery request, CancellationToken ct)
    {
        var organizations = await _deleted.ListRestorableAsync(_tenant.UserId, ct);
        return organizations
            .Select(o => new DeletedOrganizationDto(o.OrganizationId, o.Name, o.DeletedAt, o.PurgeAt))
            .ToList();
    }
}

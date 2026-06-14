using MediatR;
using Microsoft.EntityFrameworkCore;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Integrations.DTOs;

namespace Zentory.Application.Integrations.Queries;

public record GetIntegrationsQuery : IRequest<IReadOnlyList<IntegrationDto>>;

public sealed class GetIntegrationsQueryHandler
    : IRequestHandler<GetIntegrationsQuery, IReadOnlyList<IntegrationDto>>
{
    private readonly IZentoryDbContext _db;
    private readonly ITenantContext    _tenant;

    public GetIntegrationsQueryHandler(IZentoryDbContext db, ITenantContext tenant)
    {
        _db     = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<IntegrationDto>> Handle(
        GetIntegrationsQuery request,
        CancellationToken    cancellationToken)
    {
        var catalog = await _db.IntegrationCatalog
            .Where(i => i.IsEnabled && !i.IsHidden)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

        var orgConnections = await _db.OrganizationIntegrations
            .Where(oi => oi.OrganizationId == _tenant.OrganizationId && oi.IsActive)
            .ToListAsync(cancellationToken);

        var connectedMap = orgConnections.ToDictionary(oi => oi.IntegrationId);

        return catalog
            .Select(i =>
            {
                connectedMap.TryGetValue(i.Id, out var conn);
                return new IntegrationDto(
                    i.Id,
                    i.Name,
                    i.Description,
                    i.IsEnabled,
                    conn is not null,
                    conn?.ConnectedAs);
            })
            .ToList();
    }
}

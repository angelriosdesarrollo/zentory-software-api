using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zentory.Application.Common.Options;
using Zentory.Application.Organization.Services;
using Zentory.Infrastructure.Persistence;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Infrastructure.Services;

// Entrada de la lista blanca de IgnoreQueryFilters (ADR-0008 §3; RF-72): el filtro de Organization oculta justo las filas
// que hay que ver. Todas las lecturas pasan por Restorable, con el predicado explícito de RF-72 d: OwnerId del que llama,
// DeletedAt != null, dentro del plazo y PurgeStartedAt == null. Organization no tiene filtro de tenant, así que
// IgnoreQueryFilters solo quita el de borrado lógico.
public sealed class DeletedOrganizationReader : IDeletedOrganizationReader
{
    private readonly ZentoryDbContext     _db;
    private readonly OrganizationsOptions _options;
    private readonly TimeProvider         _time;

    public DeletedOrganizationReader(ZentoryDbContext db, IOptions<OrganizationsOptions> options, TimeProvider time)
    {
        _db      = db;
        _options = options.Value;
        _time    = time;
    }

    public async Task<IReadOnlyList<DeletedOrganization>> ListRestorableAsync(Guid ownerId, CancellationToken ct = default)
    {
        var rows = await Restorable(ownerId)
            .AsNoTracking()
            .OrderByDescending(o => o.DeletedAt)
            .Select(o => new { o.OrganizationId, o.Name, DeletedAt = o.DeletedAt!.Value })
            .ToListAsync(ct);

        return rows
            .Select(r => new DeletedOrganization(r.OrganizationId, r.Name, r.DeletedAt, _options.RestoreDeadline(r.DeletedAt)))
            .ToList();
    }

    public Task<OrganizationEntity?> FindRestorableAsync(Guid ownerId, Guid organizationId, CancellationToken ct = default)
        => Restorable(ownerId).FirstOrDefaultAsync(o => o.OrganizationId == organizationId, ct);

    public Task<bool> HasRestorableAsync(Guid userId, CancellationToken ct = default)
        => Restorable(userId).AnyAsync(ct);

    // Plazo abierto en el extremo: el día en que DeletedAt + PurgeAfterDays llega, la organización ya es de la purga
    // (RF-78 procesa DeletedAt <= ahora − PurgeAfterDays).
    private IQueryable<OrganizationEntity> Restorable(Guid ownerId)
    {
        var purgeThreshold = _time.GetUtcNow().UtcDateTime.AddDays(-_options.PurgeAfterDays);

        return _db.Organizations
            .IgnoreQueryFilters()
            .Where(o => o.OwnerId == ownerId
                && o.DeletedAt != null
                && o.DeletedAt > purgeThreshold
                && o.PurgeStartedAt == null);
    }
}

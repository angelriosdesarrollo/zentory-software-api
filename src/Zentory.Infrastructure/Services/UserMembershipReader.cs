using Microsoft.EntityFrameworkCore;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Infrastructure.Services;

// Entrada de la lista blanca de IgnoreQueryFilters (ADR-0008 §3): las membresías del propio usuario cruzan
// organizaciones por diseño. IgnoreQueryFilters quita también el borrado lógico, así que el predicado lo repite a mano
// (UserId del que llama y DeletedAt == null); nunca se lee por otro criterio. IgnoreQueryFilters vale para toda la
// consulta, también para el join con Organizations: su filtro de borrado lógico (DeletedAt == null) se repite aquí.
public sealed class UserMembershipReader : IUserMembershipReader
{
    private readonly ZentoryDbContext       _db;
    private readonly IPlanResolutionService _plans;

    public UserMembershipReader(ZentoryDbContext db, IPlanResolutionService plans)
    {
        _db    = db;
        _plans = plans;
    }

    public Task<IReadOnlyList<UserMembership>> ListActiveAsync(Guid userId, CancellationToken ct = default)
        => ReadAsync(userId, organizationId: null, ct);

    public async Task<UserMembership?> FindActiveAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
        => (await ReadAsync(userId, organizationId, ct)).FirstOrDefault();

    private async Task<IReadOnlyList<UserMembership>> ReadAsync(Guid userId, Guid? organizationId, CancellationToken ct)
    {
        var members = _db.OrganizationMembers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.DeletedAt == null);

        if (organizationId is { } orgId)
            members = members.Where(m => m.OrganizationId == orgId);

        var rows = await members
            .Join(_db.Organizations,
                m => m.OrganizationId,
                o => o.OrganizationId,
                (m, o) => new { m.UserId, o.OrganizationId, o.Name, o.LegalType, o.OwnerId, o.DeletedAt, m.Role, m.JoinedAt })
            // Sesión vigente del usuario en la misma consulta (PLAN-0018 RF-90 d, RNF-1; D-47): el middleware compara el
            // sid sin otra lectura. Users no tiene filtro de tenant.
            .Join(_db.Users,
                r => r.UserId,
                u => u.UserId,
                (r, u) => new { r.OrganizationId, r.Name, r.LegalType, r.OwnerId, r.DeletedAt, r.Role, r.JoinedAt, u.CurrentSessionId })
            // Nombre del owner para "Plan del propietario" (RF-76 d), con un left join en la misma consulta (D-26, RNF-1):
            // una petición con membresía cuesta esta lectura y la del plan del owner, nada más. Sin owner, sin nombre.
            .GroupJoin(_db.Users,
                r => r.OwnerId,
                u => (Guid?)u.UserId,
                (r, owners) => new { r, owners })
            .SelectMany(x => x.owners.DefaultIfEmpty(), (x, owner) => new
            {
                x.r.OrganizationId, x.r.Name, x.r.LegalType, x.r.OwnerId, x.r.DeletedAt, x.r.Role, x.r.JoinedAt,
                x.r.CurrentSessionId,
                OwnerFirstName = owner == null ? null : owner.FirstName,
                OwnerLastName  = owner == null ? null : owner.LastName,
            })
            // El filtro DeletedAt == null de Organization también queda apagado: se repite aquí (RF-65 c, D-31). Una
            // organización eliminada no aparece en GET /me/organizations, switch, login ni refresh.
            .Where(r => r.DeletedAt == null)
            .ToListAsync(ct);

        // Plan efectivo de cada owner (D-26): la segunda y última consulta, solo si hay owners.
        var ownerIds = rows.Where(r => r.OwnerId.HasValue).Select(r => r.OwnerId!.Value).Distinct().ToList();
        var plansByOwner = await _plans.ResolveForOwnersAsync(ownerIds, ct);

        return rows
            .Select(r => new UserMembership(
                r.OrganizationId,
                r.Name,
                r.LegalType,
                r.OwnerId,
                r.OwnerId.HasValue ? plansByOwner.GetValueOrDefault(r.OwnerId.Value, Plan.Free) : Plan.Free,
                r.Role,
                r.JoinedAt,
                OwnerNameOf(r.OwnerFirstName, r.OwnerLastName),
                r.CurrentSessionId))
            .ToList();
    }

    private static string? OwnerNameOf(string? firstName, string? lastName)
        => firstName is null && lastName is null ? null : $"{firstName} {lastName}".Trim();
}

using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Repositories;

public sealed class CollaboratorRepository : ICollaboratorRepository
{
    private readonly ZentoryDbContext _db;

    public CollaboratorRepository(ZentoryDbContext db) => _db = db;

    public async Task<Collaborator?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Collaborators.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Collaborator>> ListAsync(
        Guid    organizationId,
        string? search = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = _db.Collaborators
            .Where(c => c.OrganizationId == organizationId);

        if (status != null)
            query = query.Where(c => c.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.Role != null && c.Role.ToLower().Contains(term)));
        }

        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    // Entrada de la lista blanca de IgnoreQueryFilters (PLAN-0018 RF-12, ADR-0008 §3): el portal lista las
    // colaboraciones del email en todas las organizaciones (canje del magic link, cambio de organización y solicitud de
    // enlace), mientras que la sesión del colaborador solo ve su organización activa por el filtro de tenant. Como
    // IgnoreQueryFilters quita también el borrado lógico, el predicado lo repite: email normalizado, activo y no borrado,
    // y la organización no eliminada (PLAN-0018 RF-65 d, D-31): el portal deja de ofrecerla en el canje y en el cambio.
    public async Task<IReadOnlyList<Collaborator>> ListByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLower();
        return await _db.Collaborators
            .IgnoreQueryFilters()
            .Where(c => c.Email != null && c.Email.ToLower() == normalized
                && c.Status == "active" && c.DeletedAt == null
                && !_db.Organizations.Any(o => o.OrganizationId == c.OrganizationId && o.DeletedAt != null))
            .ToListAsync(ct);
    }

    public async Task<int> CountActiveAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Collaborators
            .CountAsync(c => c.OrganizationId == organizationId && c.Status == "active", ct);

    public async Task AddAsync(Collaborator collaborator, CancellationToken ct = default)
        => await _db.Collaborators.AddAsync(collaborator, ct);

    public Task UpdateAsync(Collaborator collaborator, CancellationToken ct = default)
    {
        _db.Collaborators.Update(collaborator);
        return Task.CompletedTask;
    }
}

using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Tests.Support;

// Doble en memoria de IProjectRepository con los mismos filtros que ProjectRepository
// (GetByIdAsync no filtra por organización ni por borrado, igual que el real).
public sealed class FakeProjectRepository : IProjectRepository
{
    public List<Project> Items { get; } = [];

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Project>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        IReadOnlyList<Project> result = Items
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted)
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(Project project, CancellationToken ct = default)
    {
        Items.Add(project);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Project project, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<Project>> ListAsync(
        Guid organizationId, string? search = null, string? status = null, Guid? clientId = null, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Project>> ListByClientAsync(Guid clientId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<int> CountAsync(Guid organizationId, CancellationToken ct = default)
        => throw new NotSupportedException();
}

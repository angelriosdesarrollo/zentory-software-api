namespace Zentory.Application.Organization.Services;

// Única lectura de organizaciones eliminadas (PLAN-0018 RF-72, RF-85; architecture.md §2.1, D-44; ADR-0008 §3). El filtro
// global DeletedAt == null de Organization las oculta a todo lo demás; el listado, la restauración y la emisión de la
// sesión sin organización pasan por aquí, siempre acotados al owner que llama (OwnerId == sub, SR-0018.25), dentro del
// plazo de Organizations:PurgeAfterDays y sin purga iniciada (SR-0018.27).
public interface IDeletedOrganizationReader
{
    // Eliminadas restaurables del owner, de la más reciente a la más antigua.
    Task<IReadOnlyList<DeletedOrganization>> ListRestorableAsync(Guid ownerId, CancellationToken ct = default);

    // La organización restaurable con ese id y ese owner, con seguimiento para escribirla, o null en cualquier otro caso
    // (inexistente, vigente, de otro owner, fuera de plazo o con la purga iniciada): quien llama no distingue cuál.
    Task<global::Zentory.Domain.Entities.Organization?> FindRestorableAsync(
        Guid ownerId, Guid organizationId, CancellationToken ct = default);

    // Si el usuario tiene al menos una organización propia restaurable (sesión sin organización activa, RF-85, T11b).
    Task<bool> HasRestorableAsync(Guid userId, CancellationToken ct = default);
}

public sealed record DeletedOrganization(Guid OrganizationId, string Name, DateTime DeletedAt, DateTime PurgeAt);

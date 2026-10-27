using Zentory.Domain.Repositories;

namespace Zentory.Tests.Support;

// Decorador de la unidad de trabajo para las carreras de max_owned_orgs (PLAN-0018 D-45, CA-125): antes de confirmar
// corre una vez la operación que compite (otra creación o restauración del mismo usuario, confirmada en su propio
// contexto), así el comando escribe con el OwnershipStamp ya viejo. takeHook entrega el gancho pendiente o null.
public sealed class HookedUnitOfWork(IUnitOfWork inner, Func<Func<Task>?> takeHook) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (takeHook() is { } hook)
            await hook();
        return await inner.SaveChangesAsync(ct);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
        => inner.ExecuteInTransactionAsync(operation, ct);
}

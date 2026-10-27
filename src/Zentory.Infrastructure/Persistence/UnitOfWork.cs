using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ZentoryDbContext _db;
    public UnitOfWork(ZentoryDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // InMemory no tiene transacciones (y avisa con TransactionIgnoredWarning): la operación corre igual, con la
        // misma ruta de código que en PostgreSQL salvo el Begin/Commit.
        if (!_db.Database.IsRelational())
        {
            await operation(ct);
            return;
        }

        // Una transacción ya abierta por quien llama se reutiliza: anidar Begin falla en Npgsql.
        if (_db.Database.CurrentTransaction is not null)
        {
            await operation(ct);
            return;
        }

        // La estrategia de ejecución envuelve toda la unidad: con reintentos habilitados, un fallo transitorio repite la
        // transacción completa y no solo el último comando.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await operation(ct);
            await transaction.CommitAsync(ct);
        });
    }
}

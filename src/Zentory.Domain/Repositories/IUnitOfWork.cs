namespace Zentory.Domain.Repositories;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // Ejecuta la operación en una transacción de base (PLAN-0018 A-8, D-23): eliminación de la organización, cierre de la
    // purga y reasignaciones. Confirma solo si la operación termina sin excepción; si lanza, revierte y la propaga. Con
    // un proveedor sin transacciones (EF InMemory de la suite) ejecuta la operación tal cual.
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}

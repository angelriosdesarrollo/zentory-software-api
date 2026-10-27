using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Zentory.Domain.Entities.Billing;

namespace Zentory.Tests.Support;

// Interceptor del host de pruebas (PLAN-0018 CA-44, DEC-0101; NoPlanAssignmentRouteTests): registra cada Subscription agregada, modificada o borrada al guardar, y cada
// comando SQL que nombra la tabla subscriptions con una sentencia de escritura (en PostgreSQL; InMemory no emite SQL).
internal sealed class SubscriptionWriteRecorder : SaveChangesInterceptor, IDbCommandInterceptor
{
    private static readonly Regex SqlWrite = new(
        @"\b(update|insert\s+into|delete\s+from|truncate|merge\s+into)\b[\s\S]*\bsubscriptions\b",
        RegexOptions.IgnoreCase);

    public ConcurrentQueue<string> Writes { get; } = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        RecordSql(command);
        return result;
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        RecordSql(command);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RecordSql(command);
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        RecordSql(command);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries<Subscription>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            Writes.Enqueue($"{entry.State} {nameof(Subscription)} {entry.Entity.Id}");
    }

    private void RecordSql(DbCommand command)
    {
        if (SqlWrite.IsMatch(command.CommandText)) Writes.Enqueue($"SQL {command.CommandText}");
    }
}

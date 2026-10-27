using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Zentory.Tests.Support;

// Interceptor de EF que cuenta los intentos de abrir conexión, para los tests que prueban que una operación no toca
// el servidor (PLAN-0001 CA-18). Movido de PostgresModelTests a Support/ (PLAN-0018 RF-19).
public sealed class ConnectionOpenRecorder : DbConnectionInterceptor
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Interlocked.Increment(ref _attempts);
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }
}

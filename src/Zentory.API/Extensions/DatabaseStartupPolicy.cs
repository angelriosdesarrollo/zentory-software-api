using System.Net.Sockets;
using Npgsql;

namespace Zentory.API.Extensions;

// Claves de configuración del arranque de base. Públicas para que los tests no repitan el literal.
public static class DatabaseStartupSettings
{
    public const string ConnectionStringName       = "DefaultConnection";
    public const string ConnectionStringKey        = "ConnectionStrings:" + ConnectionStringName;
    public const string AllowNonLocalDevelopmentKey = "Database:AllowNonLocalDevelopment";
}

// Qué hace el arranque con la base (PLAN-0018 D-2). Fail lleva el mensaje del error, que nombra claves y nunca valores.
public sealed record DatabaseStartupDecision(
    bool Migrate,
    bool SeedReference,
    bool SeedDemo,
    bool WriteLocalMarker,
    string? Fail)
{
    public static readonly DatabaseStartupDecision Nothing = new(false, false, false, false, null);
}

// Regla pura del arranque de base (PLAN-0018 RF-4, D-2; ADR-0007 §4). No lee configuración ni conecta: quien la aplica
// es WebApplicationExtensions.InitializeDatabaseAsync.
public static class DatabaseStartupPolicy
{
    // Verbos de la CLI del host (RF-35): con uno de ellos como primer argumento, el arranque no toca la base.
    public static readonly IReadOnlyList<string> CliVerbs = ["seed", "db"];

    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "::1"];

    public static bool IsCliVerb(string? argument)
        => argument is not null && CliVerbs.Contains(argument, StringComparer.OrdinalIgnoreCase);

    // hasAppliedMigrations solo decide WriteLocalMarker (SR-0018.33). Como leerlo exige conectar, quien aplica la regla
    // la evalúa primero con true (sin marca) y, solo si sale Migrate, lee la base y la vuelve a evaluar con el dato real.
    public static DatabaseStartupDecision Decide(
        string environment,
        bool isRelational,
        string? host,
        bool allowNonLocal,
        bool devSeedEnabled,
        string? cliVerb,
        bool hasAppliedMigrations = true)
    {
        // La CLI bifurca antes de Build(); si aun así llega un verbo, el arranque no hace nada (RF-35).
        if (!string.IsNullOrWhiteSpace(cliVerb)) return DatabaseStartupDecision.Nothing;

        // Testing, Production o cualquier otro nombre: nunca migra ni siembra al arrancar (RF-4, RF-34).
        if (!string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
            return DatabaseStartupDecision.Nothing;

        if (!isRelational)
            return new DatabaseStartupDecision(
                Migrate: false, SeedReference: true, SeedDemo: devSeedEnabled, WriteLocalMarker: false, Fail: null);

        var isLoopback = IsLoopbackHost(host);
        if (!isLoopback && !allowNonLocal)
            return DatabaseStartupDecision.Nothing with { Fail = NonLocalHostMessage };

        return new DatabaseStartupDecision(
            Migrate: true,
            SeedReference: true,
            // La demo contra un host remoto solo entra por db init --demo --allow-non-local (SR-0018.44).
            SeedDemo: devSeedEnabled && isLoopback,
            // Con el override no hay marca: una base remota nunca queda marcada como local (SR-0018.33).
            WriteLocalMarker: isLoopback && !allowNonLocal && !hasAppliedMigrations,
            Fail: null);
    }

    // Host de NpgsqlConnectionStringBuilder.Host. Varios hosts (failover) cuentan como locales solo si todos lo son.
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var entries = host.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return entries.Length > 0 && entries.All(IsLoopbackEntry);
    }

    // Lee el host sin validar el resto de la cadena; una cadena ilegible cuenta como host desconocido (no local).
    public static string? HostOf(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            return new NpgsqlConnectionStringBuilder(connectionString).Host;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // RF-5: un fallo de conexión se relanza con la clave y el tipo del error, sin el mensaje de Npgsql (lleva host y
    // puerto) ni la excepción interna. Devuelve null si la excepción no es de conexión.
    public static InvalidOperationException? ConnectionFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException or SocketException)
                return new InvalidOperationException(
                    $"Could not reach the database configured in {DatabaseStartupSettings.ConnectionStringKey} " +
                    $"({current.GetType().Name}). Check that PostgreSQL is running and that the connection string " +
                    "is correct.");
        }

        return null;
    }

    private static string NonLocalHostMessage =>
        $"The host in {DatabaseStartupSettings.ConnectionStringKey} is not a local (loopback) host: Development only " +
        $"migrates and seeds a local database. Set {DatabaseStartupSettings.AllowNonLocalDevelopmentKey}=true to " +
        "migrate a remote database on purpose (demo data is never seeded there).";

    private static bool IsLoopbackEntry(string entry)
    {
        // Socket Unix: ruta absoluta.
        if (entry.StartsWith('/')) return true;

        var name = WithoutPort(entry);
        return LoopbackHosts.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    // "localhost:5433" → "localhost"; "[::1]:5433" o "[::1]" → "::1"; "::1" se deja tal cual.
    private static string WithoutPort(string entry)
    {
        if (entry.StartsWith('['))
        {
            var close = entry.IndexOf(']');
            return close > 0 ? entry[1..close] : entry;
        }

        var colon = entry.IndexOf(':');
        return colon > 0 && colon == entry.LastIndexOf(':') ? entry[..colon] : entry;
    }
}

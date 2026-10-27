using Zentory.API.Extensions;
using Zentory.Infrastructure.Persistence.Seeding;

namespace Zentory.API.Cli;

// Estado de DemoData:Password visto por la guarda de db init --demo (RF-63 f, RF-57). El valor nunca sale de aquí.
public enum DemoPasswordStatus
{
    NotConfigured,
    LocalDefault,
    Configured,
}

// Resultado de una guarda de db init. Fail lleva el motivo (nombra flags y claves, nunca valores) y ExitCode el código
// con que termina la CLI. WriteLocalMarker solo lo decide DecidePreConnect.
public sealed record DbInitDecision(int ExitCode, string? Fail, bool WriteLocalMarker = false)
{
    public bool Proceed => Fail is null;

    public static DbInitDecision Allow(bool writeLocalMarker = false) => new(CliExitCodes.Success, null, writeLocalMarker);

    public static DbInitDecision Deny(int exitCode, string reason) => new(exitCode, reason);
}

// Regla pura de db init (PLAN-0018 RF-63 b–h; ADR-0007 §4b; D-34; SR-0018.32, SR-0018.33). No lee configuración ni
// conecta: DbCli la aplica. DecidePreConnect corre antes de abrir conexión; DecideConnected, con lo que se leyó de la base
// y antes de escribir.
public static class DbInitPolicy
{
    // Lista blanca de ambiente (SR-0018.32 a): el nombre exacto, sin ignorar mayúsculas.
    public const string RequiredEnvironment = "Development";

    public const string AllowNonLocalFlag   = "--allow-non-local";
    public const string ResetFlag           = "--reset";
    public const string DemoFlag            = "--demo";
    public const string ConfirmDatabaseFlag = "--confirm-database";

    public static DemoPasswordStatus DemoPasswordStatusOf(string? configuredPassword)
    {
        if (string.IsNullOrEmpty(configuredPassword)) return DemoPasswordStatus.NotConfigured;

        return string.Equals(configuredPassword, DemoPasswordResolver.LocalDefaultPassword, StringComparison.Ordinal)
            ? DemoPasswordStatus.LocalDefault
            : DemoPasswordStatus.Configured;
    }

    // allowNonLocalFlag es solo el flag de línea de comandos: Database:AllowNonLocalDevelopment no entra en la regla
    // (SR-0018.32 c), de ninguna fuente.
    public static DbInitDecision DecidePreConnect(
        string? environment,
        string? host,
        bool allowNonLocalFlag,
        bool reset,
        bool demo,
        DemoPasswordStatus demoPasswordStatus,
        bool interactive,
        string? confirmDatabaseArg)
    {
        if (!string.Equals(environment, RequiredEnvironment, StringComparison.Ordinal))
            return DbInitDecision.Deny(CliExitCodes.Error,
                $"db init only runs with ASPNETCORE_ENVIRONMENT={RequiredEnvironment} (exact value); nothing was opened");

        var isLoopback = DatabaseStartupPolicy.IsLoopbackHost(host);

        if (reset && allowNonLocalFlag)
            return DbInitDecision.Deny(CliExitCodes.Error,
                $"{ResetFlag} cannot be combined with {AllowNonLocalFlag}: a reset only runs against a local database");

        if (reset && !isLoopback)
            return DbInitDecision.Deny(CliExitCodes.Error,
                $"{ResetFlag} only runs against a loopback host; nothing was opened");

        if (!isLoopback && !allowNonLocalFlag)
            return DbInitDecision.Deny(CliExitCodes.Error,
                $"the host in {CliEntryPoint.ConnectionStringVariable} is not a loopback host; pass {AllowNonLocalFlag} " +
                "to initialize a remote database on purpose (configuration keys are not read for this)");

        // Fuera de local, el valor documentado es tan público como no tener contraseña (RF-57, SR-0018.44).
        if (demo && !isLoopback && demoPasswordStatus != DemoPasswordStatus.Configured)
            return DbInitDecision.Deny(CliExitCodes.Error,
                $"{DemoFlag} outside a local database needs {DemoDataSettings.PasswordKey} " +
                $"({DemoDataSettings.PasswordEnvironmentVariable}) set to a value other than the documented local default");

        // Sin terminal no hay a quién pedirle el nombre: --yes no lo reemplaza (SR-0018.32 e).
        if (reset && !interactive && string.IsNullOrWhiteSpace(confirmDatabaseArg))
            return DbInitDecision.Deny(CliExitCodes.NotConfirmed,
                $"{ResetFlag} without a terminal needs {ConfirmDatabaseFlag} <name>; {CliResults.YesFlag} does not " +
                "replace it; nothing was written");

        // Con el override no hay marca: una base remota nunca queda marcada como local (SR-0018.33).
        return DbInitDecision.Allow(writeLocalMarker: isLoopback && !allowNonLocalFlag);
    }

    // typedDatabase es lo que escribió el operador (o --confirm-database); actualDatabase, el Database de la cadena real.
    public static DbInitDecision DecideConnected(
        bool hasUsersOrOrganizations,
        bool hasLocalMarker,
        bool reset,
        string? typedDatabase,
        string? actualDatabase)
    {
        if (!reset)
            return hasUsersOrOrganizations
                ? DbInitDecision.Deny(CliExitCodes.Error,
                    $"the database already has users or organizations; only {ResetFlag} replaces them; nothing was written")
                : DbInitDecision.Allow();

        // Sin la marca puede ser una base remota vista por un túnel, o una local anterior a M3a (R-21).
        if (hasUsersOrOrganizations && !hasLocalMarker)
            return DbInitDecision.Deny(CliExitCodes.Error,
                $"{ResetFlag} needs the local database marker, and this database with data has none; nothing was written");

        var typed = typedDatabase?.Trim();
        if (string.IsNullOrEmpty(actualDatabase) || string.IsNullOrEmpty(typed)
            || !string.Equals(typed, actualDatabase, StringComparison.Ordinal))
            return DbInitDecision.Deny(CliExitCodes.NotConfirmed,
                "the database name was not confirmed (it must match the target database exactly); nothing was written");

        return DbInitDecision.Allow();
    }
}

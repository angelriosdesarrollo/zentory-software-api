using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Zentory.API.Extensions;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;

namespace Zentory.API.Cli;

// Verbo db (PLAN-0018 RF-63; ADR-0007 §4b; D-34, D-42; SR-0018.32, SR-0018.33). db init es la única vía de CLI que migra:
// guardas antes de conectar (DbInitPolicy.DecidePreConnect), lectura de la base, guardas con la base
// (DbInitPolicy.DecideConnected) y luego, en orden, --reset (revertir todas las migraciones), Migrate una sola vez, marca
// de base local, catálogos y, con --demo, los escenarios.
public static class DbCli
{
    public const string Verb = "db";
    public const string Init = "init";

    // Variable de ambiente de la lista blanca (SR-0018.32 a). Se lee del entorno de la CLI, no de un archivo.
    public const string EnvironmentVariable = "ASPNETCORE_ENVIRONMENT";

    // Línea que db init --reset imprime antes de pedir el nombre: host, puerto y base, sin usuario ni contraseña.
    public const string ResetTargetPrefix = "reset target: ";

    private static string Usage =>
        $"usage: {Verb} {Init} [{DbInitPolicy.DemoFlag}] [{DbInitPolicy.ResetFlag}] [{DbInitPolicy.AllowNonLocalFlag}] " +
        $"[{DbInitPolicy.ConfirmDatabaseFlag} <name>] [{CliResults.YesFlag}]";

    // Registro de los pasos. CliEntryPoint lo aplica antes de ConfigureServices del runtime, que puede reemplazarlos.
    public static void AddServices(IServiceCollection services)
        => services.AddScoped<IDbInitSteps, EfDbInitSteps>();

    // connectionString ya se validó en CliEntryPoint (viene solo de ConnectionStrings__DefaultConnection).
    public static async Task<int> RunAsync(string[] args, CliRuntime runtime, string connectionString)
    {
        var output = runtime.Output;
        var options = DbInitOptions.Parse(args);
        if (options is null) return CliEntryPoint.Fail(output, CliExitCodes.Usage, Usage);
        if (options.Unknown.Count > 0)
            return CliEntryPoint.Fail(output, CliExitCodes.Usage, $"unknown option(s): {string.Join(' ', options.Unknown)}");

        var configuration = CliEntryPoint.BuildConfiguration(runtime.EnvironmentVariables, connectionString);
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        runtime.EnvironmentVariables.TryGetValue(EnvironmentVariable, out var environment);

        // Guardas antes de conectar: ningún servicio se construye si fallan (RF-63 b, c, e, f).
        var preConnect = DbInitPolicy.DecidePreConnect(
            environment,
            target.Host,
            options.AllowNonLocal,
            options.Reset,
            options.Demo,
            DbInitPolicy.DemoPasswordStatusOf(configuration[DemoDataSettings.PasswordKey]),
            runtime.IsInteractive,
            options.ConfirmDatabase);
        if (!preConnect.Proceed) return CliEntryPoint.Fail(output, preConnect.ExitCode, preConnect.Fail!);

        // La contraseña demo se resuelve antes de tocar la base: si no cumple la política, nada se escribe (RF-57, D-37).
        string? demoPassword = null;
        if (options.Demo)
        {
            try
            {
                demoPassword = DemoPasswordResolver.Resolve(
                    configuration,
                    new DemoSeedTarget(environment!, IsInMemory: false, DatabaseStartupPolicy.IsLoopbackHost(target.Host)),
                    NullLogger.Instance);
            }
            catch (InvalidOperationException exception)
            {
                return CliEntryPoint.Fail(output, CliExitCodes.Error, exception.Message);
            }
        }

        string? typedDatabase = null;
        if (options.Reset)
        {
            // SR-0018.32 d: se muestra el destino completo antes de pedir el nombre, sin usuario ni contraseña.
            output.WriteLine($"{ResetTargetPrefix}host={target.Host}, port={target.Port}, database={target.Database}");
            typedDatabase = options.ConfirmDatabase ?? await PromptDatabaseNameAsync(runtime.Input, output);
        }
        else if (!options.Yes
                 && !await CliEntryPoint.ConfirmAsync(runtime.Input, output, "Apply the migrations and seed this database?"))
        {
            return CliEntryPoint.Fail(output, CliExitCodes.NotConfirmed,
                $"not confirmed; nothing was written (pass {CliResults.YesFlag} to skip the prompt)");
        }

        await using var services = CliEntryPoint.BuildServices(configuration, collection =>
        {
            AddServices(collection);
            runtime.ConfigureServices?.Invoke(collection);
        });

        try
        {
            await using var scope = services.CreateAsyncScope();
            var steps = scope.ServiceProvider.GetRequiredService<IDbInitSteps>();

            var state = await steps.InspectAsync();
            var connected = DbInitPolicy.DecideConnected(
                state.HasUsersOrOrganizations, state.HasLocalMarker, options.Reset, typedDatabase, target.Database);
            if (!connected.Proceed) return CliEntryPoint.Fail(output, connected.ExitCode, connected.Fail!);

            var changed = false;
            // El Down de cada migración recrea el esquema sin DDL a mano ni CREATEDB (ADR-0007 §4b, SR-0018.5). El esquema
            // hangfire no es de las migraciones: no se toca, y sus jobs se registran al arrancar.
            if (options.Reset) await steps.RevertAllMigrationsAsync();
            changed |= await steps.MigrateAsync() > 0;
            // Tras el reset, el Down de M3a borró la marca: se vuelve a escribir (D-42).
            if (preConnect.WriteLocalMarker) changed |= await steps.WriteLocalMarkerAsync();
            changed |= await steps.SeedReferenceAsync() > 0;
            if (demoPassword is not null) changed |= await steps.SeedDemoAsync(demoPassword) > 0;

            output.WriteLine(options.Reset ? CliResults.Replaced : changed ? CliResults.Created : CliResults.Unchanged);
            return CliExitCodes.Success;
        }
        catch (Exception exception)
        {
            return CliEntryPoint.Fail(output, CliExitCodes.Error, Describe(exception));
        }
    }

    private static async Task<string?> PromptDatabaseNameAsync(TextReader input, TextWriter output)
    {
        await output.WriteAsync("Type the database name to confirm the reset: ");
        await output.FlushAsync();
        var answer = await input.ReadLineAsync();
        output.WriteLine();
        return answer;
    }

    // Motivo sin valores de configuración (RNF-2): un fallo de conexión nombra la clave y el tipo, sin el mensaje de
    // Npgsql (trae host y puerto); un error de PostgreSQL, su SQLSTATE. PostgresException va primero: la base respondió.
    private static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres) return $"db init failed (SQLSTATE {postgres.SqlState})";
        }

        if (DatabaseStartupPolicy.ConnectionFailure(exception) is { } connection) return connection.Message;

        return $"db init failed ({exception.GetType().Name})";
    }

    private sealed record DbInitOptions(
        bool Demo, bool Reset, bool AllowNonLocal, bool Yes, string? ConfirmDatabase, IReadOnlyList<string> Unknown)
    {
        // Null si el subcomando no es init o a --confirm-database le falta el nombre.
        public static DbInitOptions? Parse(string[] args)
        {
            if (args.Length < 2 || !string.Equals(args[1], Init, StringComparison.OrdinalIgnoreCase)) return null;

            bool demo = false, reset = false, allowNonLocal = false, yes = false;
            string? confirmDatabase = null;
            var unknown = new List<string>();

            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case DbInitPolicy.DemoFlag:          demo = true; break;
                    case DbInitPolicy.ResetFlag:         reset = true; break;
                    case DbInitPolicy.AllowNonLocalFlag: allowNonLocal = true; break;
                    case CliResults.YesFlag:             yes = true; break;
                    case DbInitPolicy.ConfirmDatabaseFlag:
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return null;
                        confirmDatabase = args[++i];
                        break;
                    default: unknown.Add(args[i]); break;
                }
            }

            return new DbInitOptions(demo, reset, allowNonLocal, yes, confirmDatabase, unknown);
        }
    }
}

// Lo que db init lee de la base antes de escribir (DbInitPolicy.DecideConnected).
public sealed record DbInitState(bool HasUsersOrOrganizations, bool HasLocalMarker)
{
    public static readonly DbInitState Empty = new(false, false);
}

// Pasos de db init detrás de una interfaz: así el orden y el número de llamadas se prueban sin PostgreSQL (CA-87).
public interface IDbInitSteps
{
    Task<DbInitState> InspectAsync(CancellationToken ct = default);

    // IMigrator.MigrateAsync("0"): ejecuta el Down de todas las migraciones aplicadas.
    Task RevertAllMigrationsAsync(CancellationToken ct = default);

    // Una llamada a Migrate por ejecución. Devuelve cuántas migraciones estaban pendientes.
    Task<int> MigrateAsync(CancellationToken ct = default);

    // True si insertó la marca.
    Task<bool> WriteLocalMarkerAsync(CancellationToken ct = default);

    // Filas guardadas por ReferenceDataSeeder.
    Task<int> SeedReferenceAsync(CancellationToken ct = default);

    // Filas guardadas por los escenarios demo.
    Task<int> SeedDemoAsync(string demoPassword, CancellationToken ct = default);
}

// Pasos reales sobre el ZentoryDbContext de la composición de la CLI (sin tenant: el filtro está apagado).
public sealed class EfDbInitSteps(ZentoryDbContext db, IServiceProvider services, TimeProvider clock) : IDbInitSteps
{
    public async Task<DbInitState> InspectAsync(CancellationToken ct = default)
    {
        // Sin migraciones aplicadas no hay tablas que leer: la base está vacía y sin marca.
        if (db.Database.IsRelational() && !(await db.Database.GetAppliedMigrationsAsync(ct)).Any())
            return DbInitState.Empty;

        return new DbInitState(await HasUsersOrOrganizationsAsync(ct), await HasLocalMarkerAsync(ct));
    }

    public Task RevertAllMigrationsAsync(CancellationToken ct = default)
        => db.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase, ct);

    public async Task<int> MigrateAsync(CancellationToken ct = default)
    {
        var pending = db.Database.IsRelational() ? (await db.Database.GetPendingMigrationsAsync(ct)).Count() : 0;
        await db.GetService<IMigrator>().MigrateAsync(cancellationToken: ct);
        return pending;
    }

    public Task<bool> WriteLocalMarkerAsync(CancellationToken ct = default)
        => DatabaseMarkerWriter.EnsureLocalMarkerAsync(db, clock.GetUtcNow().UtcDateTime, ct);

    public Task<int> SeedReferenceAsync(CancellationToken ct = default)
        => CountSavedAsync(() => ActivatorUtilities.CreateInstance<ReferenceDataSeeder>(services).SeedAsync());

    public Task<int> SeedDemoAsync(string demoPassword, CancellationToken ct = default)
        => CountSavedAsync(() => services.GetRequiredService<DevDataSeeder>().SeedAsync(demoPassword, ct));

    // Una organización con borrado lógico también es una fila: se lee sin el filtro de DeletedAt (ADR-0008 §3).
    private async Task<bool> HasUsersOrOrganizationsAsync(CancellationToken ct)
        => await db.Users.AnyAsync(ct) || await db.Organizations.IgnoreQueryFilters().AnyAsync(ct);

    // Una base anterior a M3a no tiene la tabla: cuenta como "sin marca" (R-21).
    private async Task<bool> HasLocalMarkerAsync(CancellationToken ct)
    {
        try
        {
            return await db.DatabaseMarkers.AnyAsync(m => m.Environment == DatabaseMarker.LocalEnvironment, ct);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return false;
        }
    }

    // Los seeders solo guardan cuando falta algo: la suma de lo guardado distingue created de unchanged.
    private async Task<int> CountSavedAsync(Func<Task> seed)
    {
        var saved = 0;
        void Count(object? sender, SavedChangesEventArgs e) => saved += e.EntitiesSavedCount;

        db.SavedChanges += Count;
        try
        {
            await seed();
        }
        finally
        {
            db.SavedChanges -= Count;
        }

        return saved;
    }
}

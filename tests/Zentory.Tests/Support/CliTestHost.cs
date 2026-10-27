using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.API.Cli;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de la CLI (PLAN-0018 RF-35, RF-63; SeedCliTests, DbCliTests y DatabaseMarkerTests): corrida de la CLI con entrada,
// salida y entorno capturados, y una base InMemory cuyo IMigrator registra cada llamada. Ninguno conecta a una base
// real: la composición de servicios es la de producción y solo la base se reemplaza.
internal static class CliTestHost
{
    public const string EnvironmentVariable = DbCli.EnvironmentVariable;
    public const string Development         = DbInitPolicy.RequiredEnvironment;

    public const string LocalDatabase = "zentory_cli_local";
    public const string LocalPort     = "5433";
    public const string CliUser       = "cli_operator";
    public const string PasswordTrap  = "cli-password-trap";
    public const string RemoteHost    = "db-remote.invalid";

    public static readonly string LocalConnection =
        $"Host=localhost;Port={LocalPort};Database={LocalDatabase};Username={CliUser};Password={PasswordTrap}";

    public static readonly string RemoteConnection =
        $"Host={RemoteHost};Database={LocalDatabase};Username={CliUser};Password={PasswordTrap}";

    // Entorno de una corrida: la cadena (null = sin la variable), ASPNETCORE_ENVIRONMENT (null = ausente) y las extra.
    public static Dictionary<string, string?> Environment(
        string? connectionString, string? environment = Development, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (connectionString is not null) variables[CliEntryPoint.ConnectionStringVariable] = connectionString;
        if (environment is not null) variables[EnvironmentVariable] = environment;
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>()) variables[key] = value;
        return variables;
    }

    public static string AsVariable(string key) => key.Replace(":", "__", StringComparison.Ordinal);

    // Corre la CLI. onCompose se invoca solo si la CLI llega a componer servicios: si no, no pudo abrir conexión.
    public static async Task<CliRun> RunAsync(
        string[] args,
        IReadOnlyDictionary<string, string?> environment,
        CliTestDatabase? database,
        string? input = null,
        bool interactive = false,
        Action? onCompose = null,
        Action<IServiceCollection>? configure = null)
    {
        using var reader = new StringReader(input is null ? string.Empty : input + "\n");
        using var writer = new StringWriter();
        var runtime = new CliRuntime(reader, writer, environment, services =>
        {
            onCompose?.Invoke();
            database?.ReplaceDatabase(services);
            configure?.Invoke(services);
        }, interactive);

        var exitCode = await CliEntryPoint.RunAsync(args, runtime);
        return new CliRun(exitCode, writer.ToString());
    }
}

internal sealed record CliRun(int ExitCode, string Output)
{
    public IReadOnlyList<string> Lines { get; } =
        Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // La última línea es el resultado (created, unchanged, replaced o error: …).
    public string ResultLine => Lines[^1];
}

// Llamadas al IMigrator del doble: null es Migrate() hasta la última migración; "0", revertirlas todas.
internal sealed class MigratorCalls
{
    private readonly List<string?> _targets = [];
    private readonly object _gate = new();

    public IReadOnlyList<string?> Targets
    {
        get { lock (_gate) return _targets.ToList(); }
    }

    public int Count => Targets.Count;

    public void Add(string? target)
    {
        lock (_gate) _targets.Add(target);
    }
}

// IMigrator del proveedor interno de EF. Con InMemory no hay migrador, así que este es el único y todo Migrate pasa por
// él. MigrateAsync("0") hace lo que el Down de todas las migraciones: deja la base sin filas.
internal sealed class RecordingMigrator(MigratorCalls calls, ICurrentDbContext current) : IMigrator
{
    public void Migrate(string? targetMigration = null)
    {
        calls.Add(targetMigration);
        if (targetMigration == Migration.InitialDatabase) current.Context.Database.EnsureDeleted();
    }

    public async Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
    {
        calls.Add(targetMigration);
        if (targetMigration == Migration.InitialDatabase) await current.Context.Database.EnsureDeletedAsync(cancellationToken);
    }

    public string GenerateScript(
        string? fromMigration = null, string? toMigration = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        => string.Empty;
}

// Base InMemory compartida por las corridas de un test: el proveedor interno de EF (con el migrador que registra) se
// crea una vez y cada corrida reemplaza solo las opciones del DbContext de la composición de producción.
internal sealed class CliTestDatabase
{
    private readonly DbContextOptions<ZentoryDbContext> _options;

    public CliTestDatabase()
    {
        var efServices = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .AddSingleton(Migrations)
            .AddScoped<IMigrator, RecordingMigrator>()
            .BuildServiceProvider();

        _options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseInMemoryDatabase($"zentory-db-cli-{Guid.NewGuid():N}")
            .UseInternalServiceProvider(efServices)
            .Options;
    }

    public MigratorCalls Migrations { get; } = new();

    public ZentoryDbContext NewContext() => new(_options);

    public void ReplaceDatabase(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<ZentoryDbContext>>();
        services.RemoveAll<DbContextOptions>();
        services.AddSingleton(_options);
        services.AddSingleton<DbContextOptions>(_options);
    }

    // Filas de cada catálogo de ReferenceDataSeeder (seed reference, db init).
    public async Task<Dictionary<string, int>> CountCatalogsAsync()
    {
        await using var db = NewContext();
        return new Dictionary<string, int>
        {
            ["BillingPlans"]       = await db.BillingPlans.CountAsync(),
            ["PlanMarketing"]      = await db.PlanMarketing.CountAsync(),
            ["PlanFeatures"]       = await db.PlanFeatures.CountAsync(),
            ["PlanLimits"]         = await db.PlanLimits.CountAsync(),
            ["PlanCompareItems"]   = await db.PlanCompareItems.CountAsync(),
            ["SsRules"]            = await db.SsRules.CountAsync(),
            ["ExpenseCategories"]  = await db.ExpenseCategories.CountAsync(),
            ["IntegrationCatalog"] = await db.IntegrationCatalog.CountAsync(),
            ["AiProviders"]        = await db.AiProviders.CountAsync(),
            ["AiModels"]           = await db.AiModels.CountAsync(),
            ["AiFeatures"]         = await db.AiFeatures.CountAsync(),
            ["AiFeatureConfigs"]   = await db.AiFeatureConfigs.CountAsync(),
            ["AiPromptTemplates"]  = await db.AiPromptTemplates.CountAsync(),
        };
    }

    // Conteos que una corrida rechazada no debe cambiar.
    public async Task<(int Users, int Organizations, int Markers, int Plans)> CountAsync()
    {
        await using var db = NewContext();
        return (await db.Users.CountAsync(),
                await db.Organizations.IgnoreQueryFilters().CountAsync(),
                await db.DatabaseMarkers.CountAsync(),
                await db.BillingPlans.CountAsync());
    }
}

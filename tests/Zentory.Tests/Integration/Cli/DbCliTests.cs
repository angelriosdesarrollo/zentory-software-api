using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.API.Cli;
using Zentory.API.Extensions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.CliTestHost;

namespace Zentory.Tests.Integration.Cli;

// PLAN-0018 CA-86 y CA-87 (RF-63; ADR-0007 §4b; D-34; SR-0018.1, SR-0018.2, SR-0018.32): db init con entrada, salida y
// entorno capturados. La base es InMemory con un IMigrator que registra cada llamada (MigrateAsync("0") vacía la base,
// como el Down de todas las migraciones); la ejecución real contra PostgreSQL es CA-13. "Sin abrir conexión" se prueba
// con que la CLI no llegó a componer servicios: sin servicios no hay DbContext que conecte.
public sealed class DbCliTests
{
    private static readonly string[] InitYes      = [DbCli.Verb, DbCli.Init, CliResults.YesFlag];
    private static readonly string[] InitDemoYes  = [DbCli.Verb, DbCli.Init, DbInitPolicy.DemoFlag, CliResults.YesFlag];
    private static readonly string[] Reset        = [DbCli.Verb, DbCli.Init, DbInitPolicy.ResetFlag];
    private static readonly string[] ResetConfirm =
        [DbCli.Verb, DbCli.Init, DbInitPolicy.ResetFlag, DbInitPolicy.ConfirmDatabaseFlag, LocalDatabase];
    private static readonly string[] DemoResetConfirm =
        [DbCli.Verb, DbCli.Init, DbInitPolicy.DemoFlag, DbInitPolicy.ResetFlag, DbInitPolicy.ConfirmDatabaseFlag, LocalDatabase];

    // ── CA-86: ambiente ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("prod")]
    [InlineData("")]
    [InlineData(null)]
    public async Task PLAN0018_CA86_DbInit_AmbienteNoDevelopmentOAusente_TerminaDistintoDeCeroSinAbrirConexion(string? environment)
    {
        var database = new CliTestDatabase();
        var composed = false;

        var run = await RunAsync(InitYes, Environment(LocalConnection, environment), database, onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.False(composed, "db init compuso servicios (y podía conectar) fuera de Development.");
        Assert.Equal(0, database.Migrations.Count);
    }

    // ── CA-86: host no local ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA86_DbInit_HostNoLocalSinFlag_TerminaDistintoDeCeroSinConectar()
    {
        var composed = false;

        var run = await RunAsync(InitYes, Environment(RemoteConnection), new CliTestDatabase(), onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Contains(DbInitPolicy.AllowNonLocalFlag, run.ResultLine);
        Assert.False(composed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PLAN0018_CA86_DbInit_HostNoLocalConAllowNonLocalDevelopmentEnLaConfiguracionYSinFlag_LoIgnoraYNoConecta(
        bool asEnvironmentVariable)
    {
        // La CLI no lee appsettings*.json ni user-secrets (CA-43); ni siquiera como variable de entorno cuenta la clave.
        var key = asEnvironmentVariable
            ? AsVariable(DatabaseStartupSettings.AllowNonLocalDevelopmentKey)
            : DatabaseStartupSettings.AllowNonLocalDevelopmentKey;
        var composed = false;

        var run = await RunAsync(InitYes, Environment(RemoteConnection, extra: new Dictionary<string, string?> { [key] = "true" }),
            new CliTestDatabase(), onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Contains(DbInitPolicy.AllowNonLocalFlag, run.ResultLine);
        Assert.False(composed);
    }

    [Fact]
    public void PLAN0018_CA86_CodigoDeDbInit_NoLeeAllowNonLocalDevelopment()
    {
        // Test de política: el código (sin comentarios) de DbCli y DbInitPolicy no nombra la clave de configuración.
        var folder = Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Cli");
        var code = string.Join("\n", new[] { "DbCli.cs", "DbInitPolicy.cs" }
            .SelectMany(file => File.ReadAllLines(Path.Combine(folder, file)))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain(nameof(DatabaseStartupSettings.AllowNonLocalDevelopmentKey), code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowNonLocalDevelopment", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PLAN0018_CA86_DbInit_ResetConHostRemotoYOverride_TerminaDistintoDeCeroSinConectar(
        bool allowNonLocalFlag, bool allowNonLocalSetting)
    {
        var args = allowNonLocalFlag ? ResetConfirm.Append(DbInitPolicy.AllowNonLocalFlag).ToArray() : ResetConfirm;
        var extra = new Dictionary<string, string?>();
        if (allowNonLocalSetting) extra[AsVariable(DatabaseStartupSettings.AllowNonLocalDevelopmentKey)] = "true";
        var composed = false;

        var run = await RunAsync(args, Environment(RemoteConnection, extra: extra), new CliTestDatabase(),
            onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Contains(DbInitPolicy.ResetFlag, run.ResultLine);
        Assert.False(composed);
    }

    [Fact]
    public async Task PLAN0018_CA86_DbInit_ResetConHostLoopbackYFlagAllowNonLocal_TerminaDistintoDeCeroSinConectar()
    {
        var composed = false;

        var run = await RunAsync(ResetConfirm.Append(DbInitPolicy.AllowNonLocalFlag).ToArray(), Environment(LocalConnection),
            new CliTestDatabase(), onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.False(composed);
    }

    // ── CA-86: base con datos sin --reset ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PLAN0018_CA86_DbInit_BaseConUsersUOrganizationsSinReset_TerminaDistintoDeCeroYConteosIguales(
        bool withUser, bool withOrganization)
    {
        var database = new CliTestDatabase();
        await SeedForeignRowsAsync(database, withUser, withOrganization);
        var before = await database.CountAsync();

        var run = await RunAsync(InitYes, Environment(LocalConnection), database);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Contains(DbInitPolicy.ResetFlag, run.ResultLine);
        Assert.Equal(before, await database.CountAsync());
        Assert.Equal(0, database.Migrations.Count);
    }

    // ── CA-86: confirmación del --reset ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("zentory")]
    [InlineData("ZENTORY_CLI_LOCAL")]
    [InlineData("yes")]
    [InlineData(null)]
    public async Task PLAN0018_CA86_DbInit_ResetConTerminalSinNombreONombreDistinto_TerminaSinEscribir(string? typed)
    {
        var database = await DatabaseWithDemoAndMarkerAsync();
        var before = await database.CountAsync();
        var migrationsBefore = database.Migrations.Count;

        var run = await RunAsync(Reset, Environment(LocalConnection), database, input: typed, interactive: true);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Equal(before, await database.CountAsync());
        Assert.Equal(migrationsBefore, database.Migrations.Count);
    }

    [Fact]
    public async Task PLAN0018_CA86_DbInit_ResetConConfirmDatabaseDistinto_TerminaSinEscribir()
    {
        var database = await DatabaseWithDemoAndMarkerAsync();
        var before = await database.CountAsync();

        var run = await RunAsync([DbCli.Verb, DbCli.Init, DbInitPolicy.ResetFlag, DbInitPolicy.ConfirmDatabaseFlag, "zentory"],
            Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.Equal(before, await database.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PLAN0018_CA86_DbInit_ResetConYesSolo_TerminaSinEscribir(bool interactive)
    {
        // Sin terminal termina antes de conectar; con terminal, --yes no responde la pregunta del nombre.
        var database = await DatabaseWithDemoAndMarkerAsync();
        var before = await database.CountAsync();
        var migrationsBefore = database.Migrations.Count;

        var run = await RunAsync(Reset.Append(CliResults.YesFlag).ToArray(), Environment(LocalConnection), database,
            input: null, interactive: interactive);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.Equal(before, await database.CountAsync());
        Assert.Equal(migrationsBefore, database.Migrations.Count);
    }

    [Fact]
    public async Task PLAN0018_CA86_DbInit_ResetSinTerminalSinConfirmDatabase_TerminaDistintoDeCeroSinConectar()
    {
        var composed = false;

        var run = await RunAsync(Reset, Environment(LocalConnection), new CliTestDatabase(), input: LocalDatabase,
            interactive: false, onCompose: () => composed = true);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.Contains(DbInitPolicy.ConfirmDatabaseFlag, run.ResultLine);
        Assert.False(composed);
    }

    [Fact]
    public async Task PLAN0018_CA86_DbInit_Reset_ImprimeHostPuertoYBaseSinContrasenaAntesDePedirElNombre()
    {
        var database = await DatabaseWithDemoAndMarkerAsync();

        var run = await RunAsync(Reset, Environment(LocalConnection), database, input: LocalDatabase, interactive: true);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        var targetLine = run.Output.IndexOf(
            $"{DbCli.ResetTargetPrefix}host=localhost, port={LocalPort}, database={LocalDatabase}", StringComparison.Ordinal);
        var prompt = run.Output.IndexOf("Type the database name", StringComparison.Ordinal);
        Assert.True(targetLine >= 0, "db init --reset no imprimió host, puerto y base.");
        Assert.True(prompt > targetLine, "db init --reset pidió el nombre antes de mostrar el destino.");
        Assert.DoesNotContain(PasswordTrap, run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(CliUser, run.Output, StringComparison.Ordinal);
    }

    // ── CA-86: contraseña demo fuera de local ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(DemoPasswordResolver.LocalDefaultPassword)]
    public async Task PLAN0018_CA86_DbInit_DemoFueraDeLocalSinContrasenaOConLaPorDefecto_TerminaAntesDeEscribir(string? password)
    {
        var extra = new Dictionary<string, string?>();
        if (password is not null) extra[DemoDataSettings.PasswordEnvironmentVariable] = password;
        var composed = false;

        var run = await RunAsync(InitDemoYes.Append(DbInitPolicy.AllowNonLocalFlag).ToArray(),
            Environment(RemoteConnection, extra: extra), new CliTestDatabase(), onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Contains(DemoDataSettings.PasswordKey, run.ResultLine);
        Assert.False(composed);
        Assert.DoesNotContain(DemoPasswordResolver.LocalDefaultPassword, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PLAN0018_CA86_DbInit_DemoFueraDeLocalConContrasenaFueraDePolitica_TerminaAntesDeEscribir()
    {
        var composed = false;

        var run = await RunAsync(InitDemoYes.Append(DbInitPolicy.AllowNonLocalFlag).ToArray(),
            Environment(RemoteConnection, extra: new Dictionary<string, string?>
            {
                [DemoDataSettings.PasswordEnvironmentVariable] = "short",
            }),
            new CliTestDatabase(), onCompose: () => composed = true);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.False(composed);
    }

    // ── CA-87: base vacía ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA87_DbInit_BaseVacia_MigraUnaVezSiembraCatalogosEImprimeCreated()
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(InitYes, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
        Assert.Equal([null], database.Migrations.Targets);
        await using var db = database.NewContext();
        Assert.True(await db.BillingPlans.AnyAsync(), "db init no sembró los planes.");
        Assert.True(await db.PlanLimits.AnyAsync(), "db init no sembró los límites.");
        Assert.False(await db.Users.AnyAsync(), "db init sin --demo creó usuarios.");
    }

    [Fact]
    public async Task PLAN0018_CA87_DbInit_SegundaCorridaSobreBaseSinDatos_ImprimeUnchanged()
    {
        var database = new CliTestDatabase();
        await RunAsync(InitYes, Environment(LocalConnection), database);
        var afterFirst = await database.CountAsync();

        var second = await RunAsync(InitYes, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, second.ExitCode);
        Assert.Equal(CliResults.Unchanged, second.ResultLine);
        Assert.Equal(afterFirst, await database.CountAsync());
    }

    [Fact]
    public async Task PLAN0018_CA87_DbInit_Demo_DejaLosSeisEscenariosConUnSoloMigrate()
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(InitDemoYes, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
        Assert.Equal([null], database.Migrations.Targets);
        await using var db = database.NewContext();
        var organizations = await db.Organizations.IgnoreQueryFilters().Select(o => o.OrganizationId).ToListAsync();
        Assert.Equal(DemoScenarioOrganizations.All.Order(), organizations.Order());
    }

    [Fact]
    public async Task PLAN0018_CA87_DbInitDemoReset_ConfirmacionCorrectaSobreBaseConDatos_DejaExactamenteLosEscenarios()
    {
        var database = await DatabaseWithDemoAndMarkerAsync();
        var foreignUserId = await SeedForeignRowsAsync(database, withUser: true, withOrganization: true);
        var migrationsBefore = database.Migrations.Count;

        var run = await RunAsync(DemoResetConfirm, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Replaced, run.ResultLine);
        // Revertir todo y reaplicar: un solo Migrate hasta la última migración en la corrida.
        Assert.Equal([Migration.InitialDatabase, null], database.Migrations.Targets.Skip(migrationsBefore));
        await using var db = database.NewContext();
        var organizations = await db.Organizations.IgnoreQueryFilters().Select(o => o.OrganizationId).ToListAsync();
        Assert.Equal(DemoScenarioOrganizations.All.Order(), organizations.Order());
        Assert.False(await db.Users.AnyAsync(u => u.UserId == foreignUserId), "El reset conservó una fila previa.");
        Assert.True(await db.Users.AnyAsync(u => u.Email == DemoPeople.Principal.Email));
    }

    [Fact]
    public async Task PLAN0018_CA87_DbInitDemoReset_PasosEnOrden_ResetReaplicarMarcaCatalogosDemo()
    {
        var database = await DatabaseWithDemoAndMarkerAsync();
        var steps = new List<string>();

        var run = await RunAsync(DemoResetConfirm, Environment(LocalConnection), database,
            configure: services => RecordSteps(services, steps));

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(
            [nameof(IDbInitSteps.InspectAsync), nameof(IDbInitSteps.RevertAllMigrationsAsync), nameof(IDbInitSteps.MigrateAsync),
             nameof(IDbInitSteps.WriteLocalMarkerAsync), nameof(IDbInitSteps.SeedReferenceAsync), nameof(IDbInitSteps.SeedDemoAsync)],
            steps);
    }

    [Fact]
    public async Task PLAN0018_CA87_DbInit_PasosEnOrden_MigrarMarcaCatalogosSinResetNiDemo()
    {
        var steps = new List<string>();

        var run = await RunAsync(InitYes, Environment(LocalConnection), new CliTestDatabase(),
            configure: services => RecordSteps(services, steps));

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(
            [nameof(IDbInitSteps.InspectAsync), nameof(IDbInitSteps.MigrateAsync), nameof(IDbInitSteps.WriteLocalMarkerAsync),
             nameof(IDbInitSteps.SeedReferenceAsync)],
            steps);
    }

    [Fact]
    public async Task PLAN0018_CA87_DbInitYSeed_SeedNoMigraYDbInitMigraUnaVezPorEjecucion()
    {
        var database = new CliTestDatabase();

        var seed = await RunAsync([SeedCli.Verb, SeedCli.Reference, CliResults.YesFlag], Environment(LocalConnection), database);
        Assert.Equal(CliExitCodes.Success, seed.ExitCode);
        Assert.Equal(0, database.Migrations.Count);

        var init = await RunAsync(InitDemoYes, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, init.ExitCode);
        Assert.Equal([null], database.Migrations.Targets);
    }

    // ── Uso y confirmación sin --reset ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("n")]
    [InlineData(null)]
    public async Task PLAN0018_CA87_DbInit_SinYesYSinConfirmacion_TerminaSinConectar(string? answer)
    {
        var composed = false;

        var run = await RunAsync([DbCli.Verb, DbCli.Init], Environment(LocalConnection), new CliTestDatabase(), input: answer,
            onCompose: () => composed = true);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.False(composed);
    }

    [Theory]
    [InlineData(DbCli.Verb)]
    [InlineData(DbCli.Verb, "status")]
    [InlineData(DbCli.Verb, DbCli.Init, "--force")]
    [InlineData(DbCli.Verb, DbCli.Init, DbInitPolicy.ConfirmDatabaseFlag)]
    [InlineData(DbCli.Verb, DbCli.Init, DbInitPolicy.ConfirmDatabaseFlag, DbInitPolicy.ResetFlag)]
    public async Task PLAN0018_CA86_DbInit_SubcomandoUOpcionInvalida_TerminaConUsageSinConectar(params string[] args)
    {
        var composed = false;

        var run = await RunAsync(args, Environment(LocalConnection), new CliTestDatabase(), input: "yes",
            onCompose: () => composed = true);

        Assert.Equal(CliExitCodes.Usage, run.ExitCode);
        Assert.False(composed);
    }

    [Fact]
    public async Task PLAN0018_CA86_DbInit_ErrorDeConexion_ImprimeErrorSinValores()
    {
        // Npgsql contra un puerto local cerrado (sin base real): la CLI traduce el fallo sin el mensaje de Npgsql.
        var run = await RunAsync(InitYes,
            Environment($"Host=127.0.0.1;Port=1;Database={LocalDatabase};Timeout=2;Password={PasswordTrap}"), database: null);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Contains(DatabaseStartupSettings.ConnectionStringKey, run.ResultLine);
        Assert.DoesNotContain(PasswordTrap, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PLAN0018_CA86_CliEntryPoint_DespachaElVerboDbADbCli()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Cli", "CliEntryPoint.cs"));

        Assert.Contains("DbCli.RunAsync(args, runtime, connectionString)", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("is not available in this build"), code);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Base con los escenarios demo y la marca, como la deja db init --demo sobre una base vacía.
    private static async Task<CliTestDatabase> DatabaseWithDemoAndMarkerAsync()
    {
        var database = new CliTestDatabase();
        var run = await RunAsync(InitDemoYes, Environment(LocalConnection), database);
        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        return database;
    }

    // Un usuario y una organización que no son de la demo. Devuelve el id del usuario.
    private static async Task<Guid> SeedForeignRowsAsync(CliTestDatabase database, bool withUser, bool withOrganization)
    {
        await using var db = database.NewContext();
        var user = User.Create($"foreign-{Guid.NewGuid():N}@zentory.test", "Fila", "Previa", OrgRole.Member);
        if (withUser) db.Users.Add(user);
        if (withOrganization)
            db.Organizations.Add(Organization.Create($"Previa {Guid.NewGuid():N}", LegalType.Freelance));
        await db.SaveChangesAsync();
        return user.UserId;
    }

    private static void RecordSteps(IServiceCollection services, List<string> steps)
    {
        services.RemoveAll<IDbInitSteps>();
        services.AddScoped<IDbInitSteps>(sp => new RecordingSteps(ActivatorUtilities.CreateInstance<EfDbInitSteps>(sp), steps));
    }

    // Decorador de los pasos reales que anota el nombre de cada paso en el orden en que se llama.
    private sealed class RecordingSteps(IDbInitSteps inner, List<string> steps) : IDbInitSteps
    {
        public Task<DbInitState> InspectAsync(CancellationToken ct = default)
            => Record(nameof(InspectAsync), () => inner.InspectAsync(ct));

        public Task RevertAllMigrationsAsync(CancellationToken ct = default)
            => Record(nameof(RevertAllMigrationsAsync), async () => { await inner.RevertAllMigrationsAsync(ct); return 0; });

        public Task<int> MigrateAsync(CancellationToken ct = default)
            => Record(nameof(MigrateAsync), () => inner.MigrateAsync(ct));

        public Task<bool> WriteLocalMarkerAsync(CancellationToken ct = default)
            => Record(nameof(WriteLocalMarkerAsync), () => inner.WriteLocalMarkerAsync(ct));

        public Task<int> SeedReferenceAsync(CancellationToken ct = default)
            => Record(nameof(SeedReferenceAsync), () => inner.SeedReferenceAsync(ct));

        public Task<int> SeedDemoAsync(string demoPassword, CancellationToken ct = default)
            => Record(nameof(SeedDemoAsync), () => inner.SeedDemoAsync(demoPassword, ct));

        private Task<T> Record<T>(string step, Func<Task<T>> call)
        {
            steps.Add(step);
            return call();
        }
    }
}

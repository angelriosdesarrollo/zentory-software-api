using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Zentory.API.Cli;
using Zentory.API.Extensions;
using Zentory.Domain.Entities.Common;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Configurations.Core;
using Zentory.Infrastructure.Persistence.Purge;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.CliTestHost;

namespace Zentory.Tests.Integration.Cli;

// PLAN-0018 CA-124 (RF-63 h, RF-4; SR-0018.33; D-42; M3a): marca de base local en core.database_marker. db init la
// escribe sobre una base vacía y tras un --reset; --reset sobre una base con datos sin marca no escribe; ninguna migración
// la inserta; el arranque en Development la deja al crear el esquema de una base loopback.
public sealed class DatabaseMarkerTests
{
    private static readonly string[] InitYes     = [DbCli.Verb, DbCli.Init, CliResults.YesFlag];
    private static readonly string[] InitDemoYes = [DbCli.Verb, DbCli.Init, DbInitPolicy.DemoFlag, CliResults.YesFlag];
    private static readonly string[] ResetConfirm =
        [DbCli.Verb, DbCli.Init, DbInitPolicy.ResetFlag, DbInitPolicy.ConfirmDatabaseFlag, LocalDatabase];

    // ── db init ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA124_DbInit_BaseVacia_DejaUnaFilaDeMarcaConEnvironmentLocal()
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(InitYes, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        await using var db = database.NewContext();
        var marker = Assert.Single(await db.DatabaseMarkers.ToListAsync());
        Assert.Equal(DatabaseMarker.LocalEnvironment, marker.Environment);
        Assert.NotEqual(default, marker.CreatedAt);
    }

    [Fact]
    public async Task PLAN0018_CA124_DbInit_DosCorridas_SigueHabiendoUnaSolaMarca()
    {
        var database = new CliTestDatabase();

        await RunAsync(InitYes, Environment(LocalConnection), database);
        await RunAsync(InitYes, Environment(LocalConnection), database);

        await using var db = database.NewContext();
        Assert.Single(await db.DatabaseMarkers.ToListAsync());
    }

    [Fact]
    public async Task PLAN0018_CA124_DbInit_HostRemotoConAllowNonLocal_NoEscribeLaMarca()
    {
        // Una base remota nunca queda marcada como local (SR-0018.33).
        var database = new CliTestDatabase();

        var run = await RunAsync(InitYes.Append(DbInitPolicy.AllowNonLocalFlag).ToArray(), Environment(RemoteConnection), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        await using var db = database.NewContext();
        Assert.False(await db.DatabaseMarkers.AnyAsync(), "db init con --allow-non-local escribió la marca local.");
        Assert.True(await db.BillingPlans.AnyAsync());
    }

    [Fact]
    public async Task PLAN0018_CA124_DbInitReset_BaseConDatosYMarcaLoopbackConfirmada_SeEjecutaYVuelveAEscribirLaMarca()
    {
        var database = new CliTestDatabase();
        Assert.Equal(CliExitCodes.Success, (await RunAsync(InitDemoYes, Environment(LocalConnection), database)).ExitCode);
        Guid markerBefore;
        await using (var db = database.NewContext()) markerBefore = (await db.DatabaseMarkers.SingleAsync()).Id;

        var run = await RunAsync(ResetConfirm, Environment(LocalConnection), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Replaced, run.ResultLine);
        await using var check = database.NewContext();
        var marker = Assert.Single(await check.DatabaseMarkers.ToListAsync());
        Assert.Equal(DatabaseMarker.LocalEnvironment, marker.Environment);
        Assert.NotEqual(markerBefore, marker.Id);
        // Sin --demo, el reset deja solo los catálogos.
        Assert.False(await check.Users.AnyAsync());
    }

    [Fact]
    public async Task PLAN0018_CA124_DbInitReset_BaseConDatosSinMarca_TerminaDistintoDeCeroYConteosIguales()
    {
        var database = new CliTestDatabase();
        Assert.Equal(CliExitCodes.Success, (await RunAsync(InitDemoYes, Environment(LocalConnection), database)).ExitCode);
        await using (var db = database.NewContext())
        {
            db.DatabaseMarkers.RemoveRange(await db.DatabaseMarkers.ToListAsync());
            await db.SaveChangesAsync();
        }
        var before = await database.CountAsync();
        var migrationsBefore = database.Migrations.Count;

        var run = await RunAsync(ResetConfirm, Environment(LocalConnection), database);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Equal(before, await database.CountAsync());
        Assert.Equal(migrationsBefore, database.Migrations.Count);
    }

    [Fact]
    public async Task PLAN0018_CA124_DbInitReset_MarcaConOtroEnvironment_CuentaComoSinMarca()
    {
        var database = new CliTestDatabase();
        Assert.Equal(CliExitCodes.Success, (await RunAsync(InitDemoYes, Environment(LocalConnection), database)).ExitCode);
        await using (var db = database.NewContext())
        {
            var marker = await db.DatabaseMarkers.SingleAsync();
            db.Entry(marker).Property(m => m.Environment).CurrentValue = "pipeline";
            await db.SaveChangesAsync();
        }
        var before = await database.CountAsync();

        var run = await RunAsync(ResetConfirm, Environment(LocalConnection), database);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(before, await database.CountAsync());
    }

    // ── Migraciones ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA124_ScriptDeMigraciones_CreaLaTablaYNoInsertaFilasEnDatabaseMarker()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = DesignTimeContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.Contains(
            $"CREATE TABLE {DatabaseMarkerConfiguration.Schema}.{DatabaseMarkerConfiguration.TableName}", script,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex($@"INSERT\s+INTO\s+(\S+\.)?""?{DatabaseMarkerConfiguration.TableName}\b",
            RegexOptions.IgnoreCase), script);
        Assert.DoesNotMatch(new Regex($@"\b(COPY|UPDATE)\s+(\S+\.)?""?{DatabaseMarkerConfiguration.TableName}\b",
            RegexOptions.IgnoreCase), script);
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA124_MigracionAddDatabaseMarker_SinHasDataNiSqlDeInsercion()
    {
        var folder = Path.Combine(RepoRoot.Path, "src", "Zentory.Infrastructure", "Migrations");
        var migration = Assert.Single(Directory.GetFiles(folder, "*_AddDatabaseMarker.cs"));
        var code = File.ReadAllText(migration);

        Assert.DoesNotContain("InsertData", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Sql(", code, StringComparison.Ordinal);
        Assert.Contains("DropTable", code, StringComparison.Ordinal);

        using var db = TestDbContextFactory.Create();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        Assert.Empty(designModel.FindEntityType(typeof(DatabaseMarker))!.GetSeedData());
    }

    // ── Modelo: tabla global, sin tenant ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA124_DatabaseMarker_TablaCoreSinOrganizationIdNiFiltroYGlobalEnElCatalogoDePurga()
    {
        using var db = TestDbContextFactory.Create();
        var entity = db.Model.FindEntityType(typeof(DatabaseMarker))!;

        Assert.Equal(DatabaseMarkerConfiguration.TableName, entity.GetTableName());
        Assert.Equal(DatabaseMarkerConfiguration.Schema, entity.GetSchema());
        Assert.Null(entity.FindProperty(OrganizationDataCatalog.OrganizationIdProperty));
        Assert.Empty(entity.GetForeignKeys());
        Assert.Null(entity.GetQueryFilter());
        Assert.False(typeof(TenantEntity).IsAssignableFrom(typeof(DatabaseMarker)));
        Assert.Equal(OrganizationDataCategory.Global, OrganizationDataCatalog.Default.CategoryOf(typeof(DatabaseMarker)));
    }

    [Fact]
    public async Task PLAN0018_CA124_EnsureLocalMarkerAsync_EsIdempotente()
    {
        await using var db = TestDbContextFactory.Create();
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(await DatabaseMarkerWriter.EnsureLocalMarkerAsync(db, now));
        Assert.False(await DatabaseMarkerWriter.EnsureLocalMarkerAsync(db, now.AddDays(1)));

        var marker = Assert.Single(await db.DatabaseMarkers.ToListAsync());
        Assert.Equal(DatabaseMarker.LocalEnvironment, marker.Environment);
        Assert.Equal(now, marker.CreatedAt);
    }

    // ── Arranque en Development (CA-10) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA124_Arranque_PrimerArranqueDevelopmentBaseVaciaLoopback_DejaLaMarcaDespuesDeMigrar()
    {
        await using var db = TestDbContextFactory.Create();
        var steps = new List<string>();

        var decision = await WebApplicationExtensions.MigrateAndMarkAsync(
            () => { steps.Add("applied"); return Task.FromResult(false); },
            () => { steps.Add("migrate"); return Task.CompletedTask; },
            async () => { steps.Add("marker"); return await DatabaseMarkerWriter.EnsureLocalMarkerAsync(db, DateTime.UtcNow); },
            hasApplied => StartupDecision("localhost", allowNonLocal: false, hasApplied));

        Assert.True(decision.WriteLocalMarker);
        Assert.Equal(["applied", "migrate", "marker"], steps);
        var marker = Assert.Single(await db.DatabaseMarkers.ToListAsync());
        Assert.Equal(DatabaseMarker.LocalEnvironment, marker.Environment);
    }

    [Theory]
    [InlineData("localhost", false, true)]
    [InlineData("localhost", true, false)]
    [InlineData(RemoteHost, true, false)]
    public async Task PLAN0018_CA124_Arranque_BaseConMigracionesOConOverride_MigraSinMarca(
        string host, bool allowNonLocal, bool hasAppliedMigrations)
    {
        await using var db = TestDbContextFactory.Create();
        var migrated = false;

        var decision = await WebApplicationExtensions.MigrateAndMarkAsync(
            () => Task.FromResult(hasAppliedMigrations),
            () => { migrated = true; return Task.CompletedTask; },
            () => DatabaseMarkerWriter.EnsureLocalMarkerAsync(db, DateTime.UtcNow),
            hasApplied => StartupDecision(host, allowNonLocal, hasApplied));

        Assert.True(migrated);
        Assert.False(decision.WriteLocalMarker);
        Assert.False(await db.DatabaseMarkers.AnyAsync());
    }

    [Fact]
    public void PLAN0018_CA124_InitializeDatabaseAsync_EscribeLaMarcaConDatabaseMarkerWriterEnElPasoMigrate()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Extensions", "WebApplicationExtensions.cs"));
        var initialize = code.IndexOf("InitializeDatabaseAsync(", StringComparison.Ordinal);
        var migrateAndMark = code.IndexOf("await MigrateAndMarkAsync(", initialize, StringComparison.Ordinal);
        var writer = code.IndexOf("DatabaseMarkerWriter.EnsureLocalMarkerAsync(db", initialize, StringComparison.Ordinal);
        var reference = code.IndexOf("ReferenceDataSeeder", initialize, StringComparison.Ordinal);

        Assert.True(migrateAndMark > initialize, "InitializeDatabaseAsync no usa MigrateAndMarkAsync.");
        Assert.True(writer > migrateAndMark && writer < reference,
            "InitializeDatabaseAsync no escribe la marca con DatabaseMarkerWriter antes de los catálogos.");
        Assert.DoesNotContain("queda decidido pero sin efecto", code, StringComparison.Ordinal);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static DatabaseStartupDecision StartupDecision(string host, bool allowNonLocal, bool hasAppliedMigrations)
        => DatabaseStartupPolicy.Decide(Environments.Development, isRelational: true, host, allowNonLocal,
            devSeedEnabled: true, cliVerb: null, hasAppliedMigrations);

    // Contexto Npgsql sin conexión, como MigrationsModelTests y MigrationScriptTests.
    private static ZentoryDbContext DesignTimeContext(ConnectionOpenRecorder connections)
        => new(new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
            .AddInterceptors(connections)
            .Options);
}

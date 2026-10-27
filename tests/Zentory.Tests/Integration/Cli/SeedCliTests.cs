using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zentory.API.Cli;
using Zentory.API.Extensions;
using Zentory.Application.Billing.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Cli;

// PLAN-0018 (RF-35, RF-36; ADR-0002 anotado; SR-0018.1, SR-3.1, D-22): verbo seed de la CLI del host con entrada,
// salida y entorno capturados. CA-40 completo y la parte de CA-43 que cierra T23a. Ninguno conecta a una base real: la
// composición de servicios es la de producción y solo la base se reemplaza por InMemory, con un IMigrator que cuenta
// las llamadas a Migrate(). Los tests comparan valores de configuración sin imprimirlos.
public sealed class SeedCliTests
{
    private const string TargetHost     = "cli-target.invalid";
    private const string TargetDatabase = "zentory_cli";
    private const string TargetUser     = "cli_operator";
    private const string PasswordTrap   = "cli-password-trap";

    private static readonly string ConnectionString =
        $"Host={TargetHost};Database={TargetDatabase};Username={TargetUser};Password={PasswordTrap}";

    private static readonly string[] SeedReferenceYes = [SeedCli.Verb, SeedCli.Reference, CliResults.YesFlag];
    private static readonly string[] SeedReference    = [SeedCli.Verb, SeedCli.Reference];

    // ── CA-40: dos corridas sobre una base vacía ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA40_SeedReference_DosCorridasSobreBaseVacia_CreatedLuegoUnchangedConCodigoCeroYConteosIguales()
    {
        var database = new CliTestDatabase();

        var first = await RunAsync(SeedReferenceYes, database);
        var afterFirst = await database.CountCatalogsAsync();
        var second = await RunAsync(SeedReferenceYes, database);

        Assert.Equal(CliExitCodes.Success, first.ExitCode);
        Assert.Equal(CliResults.Created, first.ResultLine);
        Assert.Equal(CliExitCodes.Success, second.ExitCode);
        Assert.Equal(CliResults.Unchanged, second.ResultLine);
        Assert.All(afterFirst, catalog => Assert.True(catalog.Value > 0, $"{catalog.Key} quedó vacío tras created."));
        Assert.Equal(afterFirst, await database.CountCatalogsAsync());
    }

    [Fact]
    public async Task PLAN0018_CA40_SeedReference_FilaDeCatalogoModificadaAMano_ConservaSuValorEnLaSegundaCorrida()
    {
        var database = new CliTestDatabase();
        await RunAsync(SeedReferenceYes, database);
        var afterFirst = await database.CountCatalogsAsync();
        var edited = $"editado-{Guid.NewGuid():N}";

        await using (var db = database.NewContext())
        {
            var studio = await db.BillingPlans.SingleAsync(p => p.Name == Plan.Studio);
            db.Entry(studio).Property(p => p.DisplayName).CurrentValue = edited;
            var limit = await db.PlanLimits.FirstAsync(l => l.PlanId == studio.Id && l.LegalType == LegalType.Empresa);
            db.Entry(limit).Property(l => l.LimitValue).CurrentValue = 777;
            (await db.ExpenseCategories.OrderBy(c => c.Id).FirstAsync()).Name = edited;
            await db.SaveChangesAsync();
        }

        var second = await RunAsync(SeedReferenceYes, database);

        Assert.Equal(CliExitCodes.Success, second.ExitCode);
        Assert.Equal(CliResults.Unchanged, second.ResultLine);
        Assert.Equal(afterFirst, await database.CountCatalogsAsync());
        await using var check = database.NewContext();
        var kept = await check.BillingPlans.SingleAsync(p => p.Name == Plan.Studio);
        Assert.Equal(edited, kept.DisplayName);
        Assert.Contains(await check.PlanLimits.Where(l => l.PlanId == kept.Id).ToListAsync(), l => l.LimitValue == 777);
        Assert.Equal(edited, (await check.ExpenseCategories.OrderBy(c => c.Id).FirstAsync()).Name);
    }

    // ── CA-43 (T23a): Development + DevSeed:Enabled=true ────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA43_SeedReference_HostDevelopmentConDevSeedEnabled_NoCreaDevUserYCeroLlamadasAMigrate()
    {
        var database = new CliTestDatabase();
        var environment = CliEnvironment(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"]                 = Environments.Development,
            [CliTestHost.AsVariable(DevSeedSettings.EnabledKey)]   = "true",
        });

        var run = await RunAsync(SeedReferenceYes, database, environment: environment);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
        Assert.Equal(0, database.Migrations.Count);
        await using var db = database.NewContext();
        Assert.True(await db.BillingPlans.AnyAsync(), "seed reference no sembró los planes.");
        Assert.False(
            await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == DemoPeople.Principal.Email),
            "seed reference creó a la persona principal de la demo.");
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(), "seed reference creó usuarios.");
        Assert.False(await db.Organizations.IgnoreQueryFilters().AnyAsync(), "seed reference creó organizaciones.");
    }

    [Fact]
    public async Task PLAN0018_CA43_ContadorDeMigrate_LlamadaDirecta_CuentaUna()
    {
        // Control del anterior: el doble está en el pipeline del DbContext, así que un Migrate() de la CLI se contaría.
        var database = new CliTestDatabase();
        await using var db = database.NewContext();

        db.Database.Migrate();

        Assert.Equal(1, database.Migrations.Count);
    }

    // ── CA-43 (T23a): confirmación ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("n")]
    [InlineData("no")]
    [InlineData("si")]
    public async Task PLAN0018_CA43_SeedReference_SinYesYSinConfirmacion_TerminaConNotConfirmedSinEscribir(string answer)
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(SeedReference, database, input: answer);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.All(await database.CountCatalogsAsync(), catalog => Assert.Equal(0, catalog.Value));
    }

    [Fact]
    public async Task PLAN0018_CA43_SeedReference_SinYesYEntradaCerrada_TerminaConNotConfirmedSinEscribir()
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(SeedReference, database, input: null);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.All(await database.CountCatalogsAsync(), catalog => Assert.Equal(0, catalog.Value));
    }

    [Theory]
    [InlineData("y")]
    [InlineData("YES")]
    public async Task PLAN0018_CA43_SeedReference_SinYesConConfirmacion_Siembra(string answer)
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(SeedReference, database, input: answer);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
        await using var db = database.NewContext();
        Assert.True(await db.BillingPlans.AnyAsync());
    }

    // ── CA-43 (T23a): destino sin contraseña ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA43_SeedReference_ImprimeTargetHostYBaseSinContrasenaNiUsuario()
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(SeedReference, database, input: string.Empty);

        Assert.Equal($"{CliResults.TargetPrefix}{TargetHost}/{TargetDatabase}", run.Lines[0]);
        Assert.DoesNotContain(PasswordTrap, run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetUser, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PLAN0018_CA43_SeedReference_ErrorDeBase_ImprimeErrorConCodigoDistintoDeCeroSinValores()
    {
        // Npgsql contra un puerto local cerrado (sin base real): la CLI traduce el fallo sin el mensaje de Npgsql.
        var run = await RunAsync(SeedReferenceYes, database: null,
            environment: CliEnvironmentWithConnection(
                $"Host=127.0.0.1;Port=1;Database={TargetDatabase};Timeout=2;Password={PasswordTrap}"));

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Contains(DatabaseStartupSettings.ConnectionStringKey, run.ResultLine);
        Assert.DoesNotContain(PasswordTrap, run.Output, StringComparison.Ordinal);
    }

    // ── CA-43 (T23a): cadena solo del entorno ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PLAN0018_CA43_Cli_SinVariableDeConexion_TerminaConErrorSinConstruirServicios(string? value)
    {
        var composed = false;
        var environment = CliEnvironmentWithConnection(value);

        var run = await RunAsync(SeedReferenceYes, database: null, environment: environment, onCompose: () => composed = true);

        Assert.Equal(CliExitCodes.Error, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Contains(CliEntryPoint.ConnectionStringVariable, run.ResultLine);
        Assert.False(composed, "La CLI construyó servicios sin la variable de entorno de la cadena.");
    }

    [Fact]
    public void PLAN0018_CA43_BuildConfiguration_SoloFuentesEnMemoria_SinArchivosNiUserSecrets()
    {
        var configuration = (IConfigurationRoot)CliEntryPoint.BuildConfiguration(CliEnvironment(), ConnectionString);

        Assert.NotEmpty(configuration.Providers);
        Assert.All(configuration.Providers, provider => Assert.IsType<MemoryConfigurationProvider>(provider));
    }

    [Fact]
    public void PLAN0018_CA43_BuildConfiguration_CadenaDelEntornoComoUltimaFuente_HangfireServerYInMemoryApagados()
    {
        var environment = CliEnvironment(new Dictionary<string, string?>
        {
            // Aunque el entorno pida el servidor o InMemory, la CLI los apaga en su última fuente.
            [CliTestHost.AsVariable(HangfireSettings.ServerEnabledKey)] = "true",
            [CliTestHost.AsVariable(DatabaseSettings.UseInMemoryKey)]   = "true",
        });

        var configuration = CliEntryPoint.BuildConfiguration(environment, ConnectionString);

        Assert.True(
            configuration.GetConnectionString(DatabaseStartupSettings.ConnectionStringName) == ConnectionString,
            "La cadena de la configuración de la CLI no es la de la variable de entorno.");
        Assert.False(HangfireSettings.IsServerEnabled(configuration));
        Assert.False(DatabaseSettings.UseInMemory(configuration));
    }

    [Fact]
    public async Task PLAN0018_CA43_BuildServices_DbContextUsaNpgsqlConLaCadenaDelEntorno()
    {
        await using var services = CliEntryPoint.BuildServices(CliEntryPoint.BuildConfiguration(CliEnvironment(), ConnectionString));
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();

        Assert.True(db.Database.IsNpgsql());
        Assert.True(db.Database.GetConnectionString() == ConnectionString,
            "El DbContext de la CLI no usa la cadena de la variable de entorno.");
    }

    [Fact]
    public async Task PLAN0018_CA43_BuildConfiguration_ClaveRepetidaConGuionesYDosPuntos_NoLanzaYLosValoresDeLaCliGanan()
    {
        // En el entorno conviven "A__B", "A:B" y la misma clave con otra capitalización: son la misma clave.
        var environment = CliEnvironment(new Dictionary<string, string?>
        {
            [CliTestHost.AsVariable(HangfireSettings.ServerEnabledKey)] = "true",
            [HangfireSettings.ServerEnabledKey]             = "true",
            [CliTestHost.AsVariable(DatabaseSettings.UseInMemoryKey)]   = "true",
            [DatabaseSettings.UseInMemoryKey]               = "true",
            [DatabaseStartupSettings.ConnectionStringKey]   = "Host=duplicate-trap.invalid;Database=duplicate_trap",
            [CliTestHost.AsVariable(DevSeedSettings.EnabledKey)]        = "true",
            [DevSeedSettings.EnabledKey.ToUpperInvariant()] = "true",
        });

        var configuration = CliEntryPoint.BuildConfiguration(environment, ConnectionString);

        Assert.False(HangfireSettings.IsServerEnabled(configuration));
        Assert.False(DatabaseSettings.UseInMemory(configuration));
        Assert.True(
            configuration.GetConnectionString(DatabaseStartupSettings.ConnectionStringName) == ConnectionString,
            "Una clave repetida del entorno ganó sobre la cadena que fija la CLI.");

        var run = await RunAsync(SeedReferenceYes, new CliTestDatabase(), environment: environment);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
        Assert.DoesNotContain("duplicate-trap", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void PLAN0018_CA43_CliSources_SinArchivosNiUserSecretsYUnSoloConfigurationBuilder()
    {
        // Test de política propuesto por QA: el código de src/Zentory.API/Cli (sin comentarios) no tiene otra vía a
        // appsettings*.json, user-secrets o un host por defecto, ni lee variables sueltas fuera de CliRuntime.
        string[] forbidden =
            ["AddJsonFile", "AddUserSecrets", "AddIniFile", "AddXmlFile", "SetBasePath", "CreateDefaultBuilder",
             "CreateApplicationBuilder", "WebApplication.Create", "GetEnvironmentVariable("];
        var folder = Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Cli");
        var code = string.Join("\n", Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.All(forbidden, token => Assert.DoesNotContain(token, code, StringComparison.Ordinal));
        Assert.Single(Regex.Matches(code, @"new\s+ConfigurationBuilder\s*\("));
    }

    // ── CA-43 (T23a): sin puerto HTTP ni servidor de Hangfire ───────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA43_BuildServices_SinServidorHttpNiServidorDeHangfire()
    {
        await using var services = CliEntryPoint.BuildServices(CliEntryPoint.BuildConfiguration(CliEnvironment(), ConnectionString));

        Assert.Null(services.GetService<IServer>());
        var hangfireHostedServices = services.GetServices<IHostedService>()
            .Where(service => service.GetType().Assembly.GetName().Name?.StartsWith("Hangfire", StringComparison.Ordinal) == true)
            .Select(service => service.GetType().FullName)
            .ToList();
        Assert.Empty(hangfireHostedServices);
    }

    [Fact]
    public void PLAN0018_CA43_ProgramCs_BifurcaPorVerboAntesDeCrearElBuilderYDeBuild()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Program.cs"));

        var branch       = program.IndexOf("if (CliEntryPoint.IsCliInvocation(args))", StringComparison.Ordinal);
        var createBuilder = program.IndexOf("WebApplication.CreateBuilder(", StringComparison.Ordinal);
        var build        = program.IndexOf("builder.Build()", StringComparison.Ordinal);
        var initialize   = program.IndexOf("InitializeDatabaseAsync(", StringComparison.Ordinal);

        Assert.True(branch >= 0, "Program.cs no bifurca por el verbo de CLI.");
        Assert.True(createBuilder > branch, "La bifurcación de CLI va después de WebApplication.CreateBuilder.");
        Assert.True(build > branch, "La bifurcación de CLI va después de Build().");
        Assert.True(initialize > branch, "La bifurcación de CLI va después del arranque de base.");
        Assert.Contains("return await CliEntryPoint.RunAsync(args);", program, StringComparison.Ordinal);
    }

    // ── Uso ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("db", "status")]
    [InlineData(SeedCli.Verb, "unknown")]
    [InlineData(SeedCli.Verb, SeedCli.Reference, "--force")]
    public async Task PLAN0018_CA43_Cli_VerboOSubcomandoNoDisponible_TerminaConUsageSinEscribir(params string[] args)
    {
        var database = new CliTestDatabase();

        var run = await RunAsync(args, database, input: "yes");

        Assert.Equal(CliExitCodes.Usage, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.All(await database.CountCatalogsAsync(), catalog => Assert.Equal(0, catalog.Value));
        Assert.Equal(0, database.Migrations.Count);
    }

    [Theory]
    [InlineData(SeedCli.Verb, true)]
    [InlineData("db", true)]
    [InlineData("SEED", true)]
    [InlineData("--urls", false)]
    [InlineData("seeds", false)]
    public void PLAN0018_CA43_IsCliInvocation_PrimerArgumento_DecideLaBifurcacion(string first, bool expected)
    {
        Assert.Equal(expected, CliEntryPoint.IsCliInvocation([first]));
        Assert.False(CliEntryPoint.IsCliInvocation([]));
    }

    // ── seed assign-plan (T24: CA-41, CA-42, CA-43) ─────────────────────────────────────────────────────────────

    private const string Operator = "ops";

    private static string[] AssignPlanArgs(string email, string? plan, string? operatorName = Operator, params string[] extra)
    {
        var args = new List<string> { SeedCli.Verb, SeedCli.AssignPlan, SeedCli.EmailOption, email };
        if (plan is not null) args.AddRange([SeedCli.PlanOption, plan]);
        if (operatorName is not null) args.AddRange([SeedCli.OperatorOption, operatorName]);
        args.AddRange(extra);
        return [.. args];
    }

    [Fact]
    public async Task PLAN0018_CA41_SeedAssignPlan_StudioConYes_ImprimeCreatedYRepetirImprimeUnchangedSinEscribir()
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);

        var first = await RunAsync(AssignPlanArgs(owner.Email, Plan.Studio, Operator, CliResults.YesFlag), database);
        var afterFirst = await CountTablesAsync(database);
        var second = await RunAsync(AssignPlanArgs(owner.Email, Plan.Studio, Operator, CliResults.YesFlag), database);

        Assert.Equal(CliExitCodes.Success, first.ExitCode);
        Assert.Equal(CliResults.Created, first.ResultLine);
        Assert.Equal(CliExitCodes.Success, second.ExitCode);
        Assert.Equal(CliResults.Unchanged, second.ResultLine);
        Assert.Equal(afterFirst, await CountTablesAsync(database));
        await using var db = database.NewContext();
        var current = await db.Subscriptions.Where(s => s.UserId == owner.UserId && SubscriptionStatus.Current.Contains(s.Status))
            .Join(db.BillingPlans, s => s.PlanId, p => p.Id, (s, p) => p.Name).ToListAsync();
        Assert.Equal(Plan.Studio, Assert.Single(current));
    }

    [Fact]
    public async Task PLAN0018_CA41_SeedAssignPlan_ReasignarAPro_ImprimeReplacedYAuditaElDestinoSinContrasenaNiUsuario()
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        await RunAsync(AssignPlanArgs(owner.Email, Plan.Studio, Operator, CliResults.YesFlag), database);

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Pro, Operator, CliResults.YesFlag), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Replaced, run.ResultLine);
        await using var db = database.NewContext();
        var audit = await db.AuditLogs
            .Where(a => a.UserId == owner.UserId && a.Action == AssignPlanBySeedCommandHandler.AuditAction)
            .OrderByDescending(a => a.CreatedAt).FirstAsync();
        using var values = System.Text.Json.JsonDocument.Parse(audit.NewValues!);
        Assert.Equal($"{TargetHost}/{TargetDatabase}", values.RootElement.GetProperty("target").GetString());
        Assert.Equal(Operator, values.RootElement.GetProperty("operator").GetString());
        Assert.DoesNotContain(PasswordTrap, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(TargetUser, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(PasswordTrap, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PLAN0018_CA43_SeedAssignPlan_HostDevelopmentConDevSeedEnabled_SoloCambiaSubscriptionsBillingCustomersYAuditLog()
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        await SeedOwnerAsync(database, role: OrgRole.Member);
        var environment = CliEnvironment(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"]                           = Environments.Development,
            [CliTestHost.AsVariable(DevSeedSettings.EnabledKey)] = "true",
        });
        var before = await CountTablesAsync(database);

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Studio, Operator, CliResults.YesFlag), database,
            environment: environment);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
        Assert.Equal(
            new[] { nameof(AuditLog), nameof(BillingCustomer), nameof(Subscription) },
            TableRowCounts.Changed(before, await CountTablesAsync(database)));
        Assert.Equal(0, database.Migrations.Count);
        await using var db = database.NewContext();
        Assert.False(await db.Users.AnyAsync(u => u.Email == DemoPeople.Principal.Email),
            "seed assign-plan creó a la persona principal de la demo.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("n")]
    public async Task PLAN0018_CA43_SeedAssignPlan_SinYesYSinConfirmacion_TerminaConNotConfirmedSinEscribir(string answer)
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        var before = await CountTablesAsync(database);

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Studio), database, input: answer);

        Assert.Equal(CliExitCodes.NotConfirmed, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Equal($"{CliResults.TargetPrefix}{TargetHost}/{TargetDatabase}", run.Lines[0]);
        Assert.Contains(run.Lines, line => line.StartsWith(SeedCli.PlanPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(PasswordTrap, run.Output, StringComparison.Ordinal);
        Assert.Equal(before, await CountTablesAsync(database));
    }

    [Fact]
    public async Task PLAN0018_CA43_SeedAssignPlan_SinYesConConfirmacion_Escribe()
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Pro), database, input: "y");

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Created, run.ResultLine);
    }

    public static TheoryData<string, string?, string?, string> AssignPlanRejections => new()
    {
        // (caso, plan, operador, motivo esperado); el usuario lo arma el test según el caso.
        { "plan", "enterprise", Operator, AssignPlanBySeedCommandHandler.InvalidPlan("enterprise") },
        { "missing-user", Plan.Studio, Operator, AssignPlanBySeedCommandHandler.UserNotFound },
        { "not-owner", Plan.Studio, Operator, AssignPlanBySeedCommandHandler.NotAnOwner },
        { "operator", Plan.Studio, null, AssignPlanBySeedCommandHandler.MissingOperator },
    };

    [Theory]
    [MemberData(nameof(AssignPlanRejections))]
    public async Task PLAN0018_CA42_SeedAssignPlan_Rechazo_CodigoDistintoDeCeroErrorConMotivoYSinCambiarFilas(
        string scenario, string? plan, string? operatorName, string reason)
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        var member = await SeedOwnerAsync(database, role: OrgRole.Member);
        var email = scenario switch
        {
            "missing-user" => $"nadie-{Guid.NewGuid():N}@zentory.test",
            "not-owner"    => member.Email,
            _              => owner.Email,
        };
        var before = await CountTablesAsync(database);

        var run = await RunAsync(AssignPlanArgs(email, plan, operatorName, CliResults.YesFlag), database);

        Assert.NotEqual(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.ErrorPrefix + reason, run.ResultLine);
        Assert.Equal(before, await CountTablesAsync(database));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PLAN0018_CA42_SeedAssignPlan_DryRun_InformaLoQueHariaYNoEscribe(bool withYes)
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        var before = await CountTablesAsync(database);
        string[] extra = withYes ? [SeedCli.DryRunFlag, CliResults.YesFlag] : [SeedCli.DryRunFlag];

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Studio, Operator, extra), database, input: null);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.StartsWith(SeedCli.DryRunPrefix, run.ResultLine);
        Assert.Contains(CliResults.Created, run.ResultLine, StringComparison.Ordinal);
        Assert.Contains(run.Lines, line => line == $"{SeedCli.PlanPrefix}{owner.Email} {Plan.Free} -> {Plan.Studio}");
        Assert.Equal(before, await CountTablesAsync(database));
    }

    [Fact]
    public async Task PLAN0018_CA42_SeedAssignPlan_FreeSobreOwnerPro_ImprimeReplacedYCancelaConDowngradedBySeed()
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        await RunAsync(AssignPlanArgs(owner.Email, Plan.Pro, Operator, CliResults.YesFlag), database);

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Free, Operator, CliResults.YesFlag), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Replaced, run.ResultLine);
        await using var db = database.NewContext();
        var subscription = await db.Subscriptions.SingleAsync(s => s.UserId == owner.UserId);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(SubscriptionCancellationReason.DowngradedBySeed, subscription.CancellationReason);
    }

    [Fact]
    public async Task PLAN0018_CA42_SeedAssignPlan_FreeSobreOwnerSinSuscripcion_ImprimeUnchanged()
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        var before = await CountTablesAsync(database);

        var run = await RunAsync(AssignPlanArgs(owner.Email, Plan.Free, Operator, CliResults.YesFlag), database);

        Assert.Equal(CliExitCodes.Success, run.ExitCode);
        Assert.Equal(CliResults.Unchanged, run.ResultLine);
        Assert.Equal(before, await CountTablesAsync(database));
    }

    [Theory]
    [InlineData(SeedCli.EmailOption)]
    [InlineData(SeedCli.OperatorOption)]
    [InlineData("--force")]
    public async Task PLAN0018_CA42_SeedAssignPlan_OpcionSinValorODesconocida_TerminaConUsageSinEscribir(string option)
    {
        var database = await AssignPlanDatabaseAsync();
        var owner = await SeedOwnerAsync(database);
        var before = await CountTablesAsync(database);

        var run = await RunAsync(
            [SeedCli.Verb, SeedCli.AssignPlan, SeedCli.PlanOption, Plan.Pro, SeedCli.EmailOption, owner.Email,
             CliResults.YesFlag, option], database);

        Assert.Equal(CliExitCodes.Usage, run.ExitCode);
        Assert.StartsWith(CliResults.ErrorPrefix, run.ResultLine);
        Assert.Equal(before, await CountTablesAsync(database));
    }

    // Base con los catálogos de seed reference (BillingPlans incluidos), como la dejaría db init.
    private static async Task<CliTestDatabase> AssignPlanDatabaseAsync()
    {
        var database = new CliTestDatabase();
        var seeded = await RunAsync(SeedReferenceYes, database);
        Assert.Equal(CliExitCodes.Success, seeded.ExitCode);
        return database;
    }

    // Owner de una organización propia (o, con otro rol, miembro de una organización ajena sin ser owner de ninguna).
    private static async Task<User> SeedOwnerAsync(CliTestDatabase database, string role = OrgRole.Owner)
    {
        await using var db = database.NewContext();
        var suffix = Guid.NewGuid().ToString("N");
        var organization = Organization.Create($"Org CLI {suffix}", LegalType.Empresa);
        var user = User.Create($"{role}-cli-{suffix}@zentory.test", "Prueba", "CLI", role,
            organizationId: organization.OrganizationId);
        var owner = role == OrgRole.Owner
            ? user
            : User.Create($"owner-cli-{suffix}@zentory.test", "Prueba", "CLI", OrgRole.Owner,
                organizationId: organization.OrganizationId);
        organization.SetOwner(owner.UserId);

        db.Organizations.Add(organization);
        db.Users.Add(user);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, user.UserId, role));
        if (owner != user)
        {
            db.Users.Add(owner);
            db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));
        }
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Dictionary<string, int>> CountTablesAsync(CliTestDatabase database)
    {
        await using var db = database.NewContext();
        return await TableRowCounts.CountAsync(db);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // La corrida, la base InMemory y el migrador que registra las llamadas son los de Support/CliTestHost.
    private static Task<CliRun> RunAsync(
        string[] args,
        CliTestDatabase? database,
        string? input = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        Action? onCompose = null)
        => CliTestHost.RunAsync(args, environment ?? CliEnvironment(), database, input, onCompose: onCompose);

    // Entorno de la corrida: la variable de la cadena de prueba más las variables extra, sin ASPNETCORE_ENVIRONMENT.
    private static Dictionary<string, string?> CliEnvironment(IReadOnlyDictionary<string, string?>? extra = null)
        => CliTestHost.Environment(ConnectionString, environment: null, extra);

    // Entorno con otra cadena, o sin la variable (null).
    private static Dictionary<string, string?> CliEnvironmentWithConnection(string? connectionString)
        => CliTestHost.Environment(connectionString, environment: null);

    // Un appsettings con cadena en el directorio de trabajo (SR-3.1). El directorio de trabajo es estado global del
    // proceso: estos casos van en la colección sin paralelismo y lo restauran siempre.
    [Collection(NonParallelCollection.Name)]
    public sealed class WorkingDirectorySettings
    {
        private const string FileHost = "appsettings-trap.invalid";

        [Fact]
        public async Task PLAN0018_CA43_Cli_SinVariableYConAppsettingsConCadenaEnElDirectorio_TerminaConErrorSinUsarla()
        {
            var composed = false;
            var environment = CliEnvironmentWithConnection(null);
            environment["ASPNETCORE_ENVIRONMENT"] = Environments.Development;

            var run = await InDirectoryWithSettingsAsync(() =>
                RunAsync(SeedReferenceYes, database: null, environment: environment, onCompose: () => composed = true));

            Assert.Equal(CliExitCodes.Error, run.ExitCode);
            Assert.Contains(CliEntryPoint.ConnectionStringVariable, run.ResultLine);
            Assert.DoesNotContain(FileHost, run.Output, StringComparison.Ordinal);
            Assert.False(composed, "La CLI construyó servicios sin la variable de entorno de la cadena.");
        }

        [Fact]
        public async Task PLAN0018_CA43_Cli_ConVariableYConAppsettingsConCadenaEnElDirectorio_UsaLaDelEntorno()
        {
            var environment = CliEnvironment();
            environment["ASPNETCORE_ENVIRONMENT"] = Environments.Development;

            var run = await InDirectoryWithSettingsAsync(() =>
                RunAsync(SeedReferenceYes, new CliTestDatabase(), environment: environment));

            Assert.Equal(CliExitCodes.Success, run.ExitCode);
            Assert.Equal($"{CliResults.TargetPrefix}{TargetHost}/{TargetDatabase}", run.Lines[0]);
            Assert.DoesNotContain(FileHost, run.Output, StringComparison.Ordinal);
        }

        // appsettings.json y appsettings.Development.json de prueba (nunca los del repo) en una carpeta temporal que es
        // el directorio de trabajo durante la corrida.
        private static async Task<CliRun> InDirectoryWithSettingsAsync(Func<Task<CliRun>> run)
        {
            var folder = Path.Combine(Path.GetTempPath(), $"zentory-cli-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            var settings =
                $"{{ \"ConnectionStrings\": {{ \"DefaultConnection\": \"Host={FileHost};Database=appsettings_trap\" }} }}";
            File.WriteAllText(Path.Combine(folder, "appsettings.json"), settings);
            File.WriteAllText(Path.Combine(folder, $"appsettings.{Environments.Development}.json"), settings);

            var previous = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(folder);
                return await run();
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

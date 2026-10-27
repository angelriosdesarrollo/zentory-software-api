using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zentory.API.Extensions;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 (RF-34, RF-41; SR-3.4; SPEC-0001 CA-24 conciliado): qué siembra el host real (Program.cs) al arrancar según
// el ambiente. Production con PostgreSQL no siembra nada: Host=unused hace que cualquier seed tenga que abrir una
// conexión, así que cero aperturas prueban que ni ReferenceDataSeeder ni los escenarios corrieron. Development con
// InMemory siembra siempre los catálogos y la demo solo con DevSeed:Enabled. Ningún test conecta a una base real.
//
// Sin paralelismo: los hosts registran Hangfire, que fija JobStorage.Current global del proceso (R-6).
[Collection(NonParallelCollection.Name)]
public sealed class SeedingStartupTests
{
    private const string UnusedConnectionString = "Host=unused;Database=zentory_unused;Username=zentory";

    private static readonly string[] BillingPlanNames = [Plan.Free, Plan.Pro, Plan.Studio];

    // ── CA-39: Production no siembra ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA39_ProductionHost_ConNpgsqlHostUnusedYHangfireApagado_NoSiembraNiAbreConexiones()
    {
        var recorder = new ConnectionOpenRecorder();
        await using var host = new MinimalHostFactory(Environments.Production, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]             = "false",
            [DatabaseStartupSettings.ConnectionStringKey] = UnusedConnectionString,
            [HangfireSettings.ServerEnabledKey]           = "false",
            // Encendido a propósito: fuera de Development el flag no debe bastar para sembrar la demo.
            [DevSeedSettings.EnabledKey]                  = "true",
        });
        await using var factory = host.WithConnectionRecorder(recorder);

        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.Equal(Environments.Production, factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.True(db.Database.IsNpgsql());
        // El contador está en el pipeline del DbContext: si un seed hubiera corrido, habría contado su apertura.
        ConnectionRecorderWiring.AssertWired(db, recorder);
        Assert.Equal(0, recorder.Attempts);
    }

    // ── CA-39: Development InMemory sin demo ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA39_DevelopmentInMemory_ConDevSeedDisabled_SiembraLosTresBillingPlansSinDevUser()
    {
        await using var factory = DevelopmentInMemoryHost(devSeedEnabled: false);
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.True(db.Database.IsInMemory());
        Assert.Equal(BillingPlanNames.Order(), (await db.BillingPlans.Select(p => p.Name).ToListAsync()).Order());
        Assert.True(await db.PlanLimits.AnyAsync(), "El arranque no sembró los límites de los planes.");
        Assert.False(
            await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == DemoPeople.Principal.Email),
            "El arranque sin DevSeed creó a la persona principal de la demo.");
        Assert.False(await db.Organizations.IgnoreQueryFilters().AnyAsync(), "El arranque sin DevSeed creó organizaciones.");
    }

    [Fact]
    public async Task PLAN0018_CA39_DevelopmentInMemory_CatalogoSembradoAlArrancar_MaxProjectsSinLimiteEnTodosLosPlanes()
    {
        // RF-41: FEATURE_PERMISSIONS §3 dice «Sin límite» de proyectos en los seis estados legalType × plan.
        await using var factory = DevelopmentInMemoryHost(devSeedEnabled: false);
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var projectLimits = await db.PlanLimits
            .Where(l => l.FeatureKey == PlanLimits.FeatureKeys.MaxProjects)
            .Join(db.BillingPlans, l => l.PlanId, p => p.Id, (l, p) => new { Plan = p.Name, l.LegalType, l.LimitValue })
            .ToListAsync();

        var expectedStates = BillingPlanNames
            .SelectMany(plan => new[] { LegalType.Freelance, LegalType.Empresa }.Select(legalType => (plan, legalType)))
            .Order()
            .ToList();
        Assert.Equal(expectedStates, projectLimits.Select(l => (l.Plan, l.LegalType)).Order().ToList());
        Assert.All(projectLimits, l => Assert.Null(l.LimitValue));
    }

    // ── CA-39: Development InMemory con demo ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA39_DevelopmentInMemory_ConDevSeedEnabled_ExistenLosSeisEscenariosYElDevUser()
    {
        await using var factory = DevelopmentInMemoryHost(devSeedEnabled: true);
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organizations = await db.Organizations.IgnoreQueryFilters().Select(o => o.OrganizationId).ToListAsync();
        Assert.Equal(DemoScenarioOrganizations.All.Order(), organizations.Order());

        var principal = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == DemoPeople.Principal.Email);
        Assert.True(principal is not null, "El arranque con DevSeed no creó a la persona principal de la demo.");
        Assert.Equal(4, await db.OrganizationMembers.IgnoreQueryFilters().CountAsync(m => m.UserId == principal!.UserId));
        Assert.Equal(BillingPlanNames.Length, await db.BillingPlans.CountAsync());
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Development con una base InMemory propia y Hangfire apagado. DemoData:Password llega vacía (la neutraliza
    // MinimalHostFactory), así que la demo usa el valor local documentado y ningún test lee ni imprime contraseñas.
    private static MinimalHostFactory DevelopmentInMemoryHost(bool devSeedEnabled)
        => new(Environments.Development, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-seeding-startup-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = devSeedEnabled ? "true" : "false",
        });
}

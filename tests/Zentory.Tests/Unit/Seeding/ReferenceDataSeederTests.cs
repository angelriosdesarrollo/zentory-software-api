using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zentory.API.Extensions;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Seeding;

// PLAN-0018 (RF-33, D-5; parte de CA-12, que se cierra en T4c con los escenarios demo): ReferenceDataSeeder inserta
// por clave natural solo lo que falta y nunca modifica filas existentes. Cada test usa su propia base InMemory; la
// segunda corrida va con un contexto nuevo para que el estado del change tracker no esconda duplicados.
public sealed class ReferenceDataSeederTests
{
    // Filas por catálogo sobre una base vacía: el mismo contenido que sembraba SeedMasterDataAsync (refactor sin cambio
    // de conducta).
    private static readonly IReadOnlyDictionary<string, int> ExpectedCounts = new Dictionary<string, int>
    {
        ["BillingPlans"]       = 3,
        ["PlanMarketing"]      = 6,
        ["PlanFeatures"]       = 28,
        ["PlanLimits"]         = 36,
        ["PlanCompareItems"]   = 15,
        ["SsRules"]            = 8,
        ["ExpenseCategories"]  = 12,
        ["IntegrationCatalog"] = 4,
        ["AiProviders"]        = 1,
        ["AiModels"]           = 1,
        ["AiFeatures"]         = 1,
        ["AiFeatureConfigs"]   = 1,
        ["AiPromptTemplates"]  = 1,
    };

    [Fact]
    public async Task PLAN0018_CA12_ReferenceDataSeeder_SobreBaseVacia_SiembraTodosLosCatalogos()
    {
        var databaseName = NewDatabaseName();

        await SeedAsync(databaseName);

        Assert.Equal(ExpectedCounts, await CountCatalogsAsync(databaseName));
    }

    [Fact]
    public async Task PLAN0018_CA12_ReferenceDataSeeder_SegundaCorrida_NoCambiaElNumeroDeFilasDeNingunCatalogo()
    {
        var databaseName = NewDatabaseName();
        await SeedAsync(databaseName);
        var afterFirstRun = await CountCatalogsAsync(databaseName);

        await SeedAsync(databaseName);

        Assert.Equal(afterFirstRun, await CountCatalogsAsync(databaseName));
    }

    [Fact]
    public async Task PLAN0018_CA12_ReferenceDataSeeder_FilasDeCatalogoModificadas_ConservanSuValorEnLaSegundaCorrida()
    {
        var databaseName = NewDatabaseName();
        await SeedAsync(databaseName);
        var afterFirstRun = await CountCatalogsAsync(databaseName);
        var edited = $"editado-{Guid.NewGuid():N}";

        // Se edita una fila de cada forma de clave natural: por nombre, compuesta, por posición, por Id fijo y por slug.
        await using (var db = NewContext(databaseName))
        {
            var plan = await db.BillingPlans.SingleAsync(p => p.Name == Plan.Studio);
            db.Entry(plan).Property(p => p.DisplayName).CurrentValue = edited;

            var limit = await db.PlanLimits.FirstAsync(l => l.PlanId == plan.Id && l.LegalType == LegalType.Empresa);
            db.Entry(limit).Property(l => l.LimitValue).CurrentValue = 777;

            var feature = await db.PlanFeatures.FirstAsync(f => f.PlanId == plan.Id && f.LegalType == LegalType.Freelance);
            db.Entry(feature).Property(f => f.Text).CurrentValue = edited;

            var compare = await db.PlanCompareItems.FirstAsync(c => c.LegalType == null);
            db.Entry(compare).Property(c => c.FeatureName).CurrentValue = edited;

            (await db.SsRules.FirstAsync()).Notes = edited;
            (await db.ExpenseCategories.OrderBy(c => c.Id).FirstAsync()).Name = edited;

            var integration = await db.IntegrationCatalog.OrderBy(i => i.SortOrder).FirstAsync();
            db.Entry(integration).Property(i => i.Name).CurrentValue = edited;

            var model = await db.AiModels.SingleAsync();
            db.Entry(model).Property(m => m.DisplayName).CurrentValue = edited;

            await db.SaveChangesAsync();
        }

        await SeedAsync(databaseName);

        Assert.Equal(afterFirstRun, await CountCatalogsAsync(databaseName));
        await using var check = NewContext(databaseName);
        var studio = await check.BillingPlans.SingleAsync(p => p.Name == Plan.Studio);
        Assert.Equal(edited, studio.DisplayName);
        Assert.Contains(await check.PlanLimits.Where(l => l.PlanId == studio.Id).ToListAsync(), l => l.LimitValue == 777);
        Assert.Contains(await check.PlanFeatures.Where(f => f.PlanId == studio.Id).ToListAsync(), f => f.Text == edited);
        Assert.Contains(await check.PlanCompareItems.ToListAsync(), c => c.FeatureName == edited);
        Assert.Contains(await check.SsRules.ToListAsync(), r => r.Notes == edited);
        Assert.Equal(edited, (await check.ExpenseCategories.OrderBy(c => c.Id).FirstAsync()).Name);
        Assert.Equal(edited, (await check.IntegrationCatalog.OrderBy(i => i.SortOrder).FirstAsync()).Name);
        Assert.Equal(edited, (await check.AiModels.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task PLAN0018_CA12_ReferenceDataSeeder_FilaDeCatalogoBorrada_SeReinsertaSinDuplicarLasDemas()
    {
        var databaseName = NewDatabaseName();
        await SeedAsync(databaseName);
        var afterFirstRun = await CountCatalogsAsync(databaseName);

        await using (var db = NewContext(databaseName))
        {
            db.PlanLimits.Remove(await db.PlanLimits.FirstAsync());
            db.IntegrationCatalog.Remove(await db.IntegrationCatalog.FirstAsync());
            db.AiPromptTemplates.Remove(await db.AiPromptTemplates.SingleAsync());
            await db.SaveChangesAsync();
        }

        await SeedAsync(databaseName);

        Assert.Equal(afterFirstRun, await CountCatalogsAsync(databaseName));
    }

    [Fact]
    public async Task PLAN0018_CA12_ReferenceDataSeeder_ConIntegracionesDelHasData_NoLasDuplica()
    {
        // EnsureCreated aplica el HasData del modelo, como la migración inicial en PostgreSQL.
        var databaseName = NewDatabaseName();
        await using (var db = NewContext(databaseName))
        {
            await db.Database.EnsureCreatedAsync();
            Assert.Equal(ExpectedCounts["IntegrationCatalog"], await db.IntegrationCatalog.CountAsync());
        }

        await SeedAsync(databaseName);

        Assert.Equal(ExpectedCounts, await CountCatalogsAsync(databaseName));
    }

    [Fact]
    public async Task PLAN0018_CA12_DevelopmentInMemorySinDemo_ElArranqueSiembraCatalogosSinPersonasDemo()
    {
        // SeedReference sin SeedDemo (DevSeed:Enabled=false): el arranque corre ReferenceDataSeeder y no DevDataSeeder.
        await using var factory = new MinimalHostFactory(Environments.Development, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = NewDatabaseName(),
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
        });
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.Equal(ExpectedCounts["BillingPlans"], await db.BillingPlans.CountAsync());
        Assert.Equal(ExpectedCounts["PlanLimits"], await db.PlanLimits.CountAsync());
        Assert.False(await db.Organizations.IgnoreQueryFilters().AnyAsync(), "La base tiene organizaciones demo.");
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(), "La base tiene usuarios demo.");
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static string NewDatabaseName() => $"zentory-reference-{Guid.NewGuid():N}";

    // Mismo nombre de base InMemory = misma base, con un contexto (y un change tracker) nuevo en cada llamada.
    private static ZentoryDbContext NewContext(string databaseName)
        => new(new DbContextOptionsBuilder<ZentoryDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static async Task SeedAsync(string databaseName)
    {
        await using var db = NewContext(databaseName);
        await new ReferenceDataSeeder(db).SeedAsync();
    }

    private static async Task<Dictionary<string, int>> CountCatalogsAsync(string databaseName)
    {
        await using var db = NewContext(databaseName);
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
}

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Seeding;

// PLAN-0018 (RF-6, RF-33, RF-56 a; D-5): cierre de CA-12. ReferenceDataSeeder y los seis escenarios demo corren dos veces
// sobre la misma base InMemory (otro día, otro contexto, otra contraseña): ninguna tabla del modelo gana filas, toda
// fecha escrita es UTC y las filas que alguien editó entre corridas conservan su valor. Las tablas se recorren por el
// modelo de EF, así que una entidad nueva queda cubierta sin tocar este archivo.
public sealed class SeedIdempotencyTests
{
    private static readonly DateTimeOffset FirstRun = new(2026, 10, 24, 14, 30, 0, TimeSpan.Zero);

    // Otro mes: las fechas relativas cambian y las facturas de la Consultora caerían en otro mes si se reinsertaran.
    private static readonly DateTimeOffset SecondRun = FirstRun.AddDays(41);

    [Fact]
    public async Task PLAN0018_CA12_CatalogosYEscenarios_SegundaCorridaSobreLaMismaBase_NoCambianLasFilasDeNingunaTabla()
    {
        var databaseName = NewDatabaseName();
        await SeedAllAsync(databaseName, FirstRun);
        var afterFirstRun = await CountAllTablesAsync(databaseName);

        await SeedAllAsync(databaseName, SecondRun);

        Assert.Equal(afterFirstRun, await CountAllTablesAsync(databaseName));
        // La primera corrida sembró de verdad: catálogos, las seis organizaciones y su contenido.
        Assert.Equal(6, afterFirstRun["Organization"]);
        foreach (var table in new[] { "BillingPlan", "PlanLimit", "SsRule", "Organization", "User", "OrganizationMember",
                     "Client", "Project", "Invoice", "TimeEntry", "Subscription", "SsCalculationLog", "CashFlowEntry" })
            Assert.True(afterFirstRun.GetValueOrDefault(table) > 0, $"La siembra no escribió filas en {table}.");
    }

    [Fact]
    public async Task PLAN0018_CA12_CatalogosYEscenarios_DosCorridas_NingunaFechaEscritaTieneKindDistintoDeUtc()
    {
        var databaseName = NewDatabaseName();
        await SeedAllAsync(databaseName, FirstRun);
        await SeedAllAsync(databaseName, SecondRun);

        await using var db = NewContext(databaseName);
        var (checkedValues, offending) = await NonUtcDateTimesAsync(db);

        Assert.True(checkedValues > 500, $"Se revisaron muy pocas fechas ({checkedValues}).");
        Assert.True(offending.Count == 0, "Fechas sin Kind = Utc: " + string.Join(", ", offending.Distinct()));
    }

    [Fact]
    public async Task PLAN0018_CA12_FilasDeCatalogoYDeEscenarioModificadas_ConservanSuValorEnLaSegundaCorrida()
    {
        var databaseName = NewDatabaseName();
        await SeedAllAsync(databaseName, FirstRun);
        var edited = $"editado-{Guid.NewGuid():N}";
        var clientId = DemoIds.For(ConsultancyScenario.OrganizationId, "client:textiles");

        await using (var db = NewContext(databaseName))
        {
            var plan = await db.BillingPlans.SingleAsync(p => p.Name == Plan.Free);
            db.Entry(plan).Property(p => p.DisplayName).CurrentValue = edited;

            var limit = await db.PlanLimits.SingleAsync(l =>
                l.PlanId == plan.Id && l.LegalType == LegalType.Empresa && l.FeatureKey == PlanLimits.FeatureKeys.MaxClients);
            db.Entry(limit).Property(l => l.LimitValue).CurrentValue = 9;

            var client = await db.Clients.IgnoreQueryFilters().SingleAsync(c => c.Id == clientId);
            db.Entry(client).Property(c => c.Name).CurrentValue = edited;
            await db.SaveChangesAsync();
        }

        await SeedAllAsync(databaseName, SecondRun);

        await using var check = NewContext(databaseName);
        var freePlan = await check.BillingPlans.SingleAsync(p => p.Name == Plan.Free);
        Assert.Equal(edited, freePlan.DisplayName);
        Assert.Equal(9, (await check.PlanLimits.SingleAsync(l =>
            l.PlanId == freePlan.Id && l.LegalType == LegalType.Empresa && l.FeatureKey == PlanLimits.FeatureKeys.MaxClients)).LimitValue);
        Assert.Equal(edited, (await check.Clients.IgnoreQueryFilters().SingleAsync(c => c.Id == clientId)).Name);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static string NewDatabaseName() => $"zentory-seed-{Guid.NewGuid():N}";

    // Contraseña válida y distinta en cada corrida: nunca es un literal del archivo ni se imprime.
    private static string NewValidPassword() => $"Seed-{Guid.NewGuid():N}"[..20];

    private static ZentoryDbContext NewContext(string databaseName)
        => new(new DbContextOptionsBuilder<ZentoryDbContext>().UseInMemoryDatabase(databaseName).Options);

    // Una corrida completa, como el arranque: catálogos y luego escenarios, cada uno con un contexto nuevo para que el
    // change tracker no esconda duplicados.
    private static async Task SeedAllAsync(string databaseName, DateTimeOffset now)
    {
        await using (var db = NewContext(databaseName))
            await new ReferenceDataSeeder(db).SeedAsync();

        await using var demo = NewContext(databaseName);
        await new DemoScenarioRunner(DemoScenarioRunner.DefaultScenarios).RunAsync(demo, new FakeTimeProvider(now), NewValidPassword());
    }

    // Filas por tipo de entidad del modelo, sin filtros de consulta.
    private static async Task<Dictionary<string, int>> CountAllTablesAsync(string databaseName)
    {
        await using var db = NewContext(databaseName);
        var counts = new Dictionary<string, int>();
        foreach (var clrType in EntityTypes(db))
        {
            var rows = await LoadAsync(db, clrType);
            counts[clrType.Name] = rows.Count;
        }

        return counts;
    }

    private static async Task<(int Checked, List<string> Offending)> NonUtcDateTimesAsync(ZentoryDbContext db)
    {
        var checkedValues = 0;
        var offending = new List<string>();
        foreach (var clrType in EntityTypes(db))
        {
            var dateProperties = clrType.GetProperties()
                .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?))
                .ToList();
            if (dateProperties.Count == 0) continue;

            foreach (var row in await LoadAsync(db, clrType))
            foreach (var property in dateProperties)
            {
                if (property.GetValue(row) is not DateTime value) continue;
                checkedValues++;
                if (value.Kind != DateTimeKind.Utc) offending.Add($"{clrType.Name}.{property.Name}");
            }
        }
        return (checkedValues, offending);
    }

    private static IEnumerable<Type> EntityTypes(ZentoryDbContext db)
        => db.Model.GetEntityTypes().Where(t => !t.IsOwned() && !t.HasSharedClrType).Select(t => t.ClrType).Distinct();

    private static Task<List<object>> LoadAsync(ZentoryDbContext db, Type clrType)
        => (Task<List<object>>)typeof(SeedIdempotencyTests)
            .GetMethod(nameof(LoadAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(clrType)
            .Invoke(null, [db])!;

    private static async Task<List<object>> LoadAllAsync<T>(ZentoryDbContext db) where T : class
        => await db.Set<T>().IgnoreQueryFilters().AsNoTracking().Cast<object>().ToListAsync();
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;
using Zentory.Infrastructure.Persistence.Seeding.Demo;

namespace Zentory.Tests.Support;

// Catálogo de referencia (PlanLimits incluidos) y escenarios demo sembrados en la base del host de pruebas, una vez por
// base (PLAN-0018 RF-56; CA-46, CA-88, CA-99). Va antes de cualquier TenantBuilder, que crearía BillingPlan sin límites.
// Los dos seeders son idempotentes; la Consultora sembrada marca que la base ya está lista. Lo usan PlanLimitsTests e
// InviteMemberTests.
public static class DemoScenarioSeed
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    public static async Task EnsureAsync(IServiceProvider services)
    {
        await SeedLock.WaitAsync();
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            if (await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.OrganizationId == ConsultancyScenario.OrganizationId))
                return;
            await new ReferenceDataSeeder(db).SeedAsync();
            db.ChangeTracker.Clear();
            await new DemoScenarioRunner(DemoScenarioRunner.DefaultScenarios)
                .RunAsync(db, TimeProvider.System, $"Demo-{Guid.NewGuid():N}"[..20]);
        }
        finally
        {
            SeedLock.Release();
        }
    }
}

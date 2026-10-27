using Zentory.Infrastructure.Persistence.Seeding.Demo;

namespace Zentory.Infrastructure.Persistence;

/// <summary>
/// Siembra la demo de desarrollo: queda como orquestador de los escenarios versionados (PLAN-0018 RF-56) en
/// <c>Persistence/Seeding/Demo/</c>, con fechas relativas al <see cref="TimeProvider"/> (D-5, D-35).
/// Los catálogos los siembra antes <see cref="Seeding.ReferenceDataSeeder"/> (RF-33): la demo los necesita.
/// Va directo a <see cref="ZentoryDbContext"/>, sin ITenantContext.
/// </summary>
public sealed class DevDataSeeder
{
    // ProjectExpenseStore guarda su semilla con esta organización; ExpenseTenantIsolationTests compara los dos valores
    // por reflexión, así que el nombre y la visibilidad del campo se conservan.
    private static readonly Guid DevOrgId = AgencyScenario.OrganizationId;

    private readonly ZentoryDbContext _db;
    private readonly TimeProvider     _timeProvider;

    public DevDataSeeder(ZentoryDbContext db, TimeProvider timeProvider)
    {
        _db           = db;
        _timeProvider = timeProvider;
    }

    /// <param name="demoPassword">Ya resuelta y validada por DemoPasswordResolver (D-37); nunca se registra.</param>
    public Task SeedAsync(string demoPassword, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(demoPassword);
        return new DemoScenarioRunner(DemoScenarioRunner.DefaultScenarios).RunAsync(_db, _timeProvider, demoPassword, ct);
    }
}

using Zentory.Infrastructure.Persistence.Seeding.Demo;

namespace Zentory.Tests.Support;

// Las seis organizaciones de los escenarios demo de §3.0.A (PLAN-0018 RF-56), en el orden de
// DemoScenarioRunner.DefaultScenarios. Compartido por DemoScenariosTests y SeedingStartupTests.
internal static class DemoScenarioOrganizations
{
    public static readonly Guid[] All =
    [
        AgencyScenario.OrganizationId, FreelanceBrandScenario.OrganizationId, ConstructionScenario.OrganizationId,
        ConsultancyScenario.OrganizationId, FreelanceOtherScenario.OrganizationId, FreelanceNewScenario.OrganizationId,
    ];
}

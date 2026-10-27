using Zentory.Domain.Constants;

namespace Zentory.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Freelance nueva (§3.0.A): freelance · free, recién registrada. Solo la organización, su owner y la membresía: sin
/// clientes, proyectos, configuración ni <c>profile.work_type</c>, para recorrer <c>/onboarding/work-type</c>. Su owner
/// nunca tuvo suscripción, así que el plan resuelto es free.
/// </summary>
public sealed class FreelanceNewScenario : IDemoScenario
{
    public const string OrganizationName = "Natalia Gil";

    public static readonly Guid OrganizationId = new("a1000000-0000-0000-0000-000000000007");

    public static readonly DemoPerson Owner = new(
        new Guid("b2000000-0000-0000-0000-000000000008"), DemoPeople.Email("natalia.gil"), "Natalia", "Gil");

    public string Name => OrganizationName;

    public async Task SeedAsync(DemoSeedContext context, CancellationToken ct = default)
    {
        await context.EnsureUserAsync(Owner, OrgRole.Owner, ct);
        await context.EnsureOrganizationAsync(OrganizationId, OrganizationName, LegalType.Freelance, Owner.Id, ct);
        await context.EnsureMembershipAsync(OrganizationId, Owner.Id, OrgRole.Owner, -1, ct);
    }
}

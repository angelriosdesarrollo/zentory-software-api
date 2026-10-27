using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// Apoyo de los tests de organizaciones eliminadas y restauración (PLAN-0018 RF-72, RF-85): rutas, la sesión sin
// organización activa firmada a mano (TestUserJwt, sin pasar por login), el borrado
// lógico directo con una fecha dada, el inicio de la purga y el límite max_owned_orgs de un plan.
public static class OrganizationRestoreApi
{
    public const string DeletedEndpoint = "/api/v1/me/organizations/deleted";

    public static string RestoreEndpoint(Guid organizationId) => $"/api/v1/organizations/{organizationId}/restore";

    public static Task<HttpResponseMessage> RestoreAsync(HttpClient http, Guid organizationId)
        => http.PostAsync(RestoreEndpoint(organizationId), content: null);

    // Access token del esquema de usuario sin active_org_id (RF-85, D-44): solo sub, email, iniciales y el sid de la
    // sesión vigente del usuario (RF-90, D-47).
    public static string TokenWithoutOrganization(IServiceProvider services, User user) => TestUserJwt.Sign(services, user);

    // Borrado lógico fuera de una petición, con la fecha que pida el test (DELETE /organization usa el reloj del host).
    public static Task MarkDeletedAsync(IServiceProvider services, Guid organizationId, Guid deletedBy, DateTime deletedAtUtc)
        => ChangeAsync(services, organizationId, (_, organization) => organization.MarkDeleted(deletedBy, deletedAtUtc));

    // Lo que hará la purga de RF-78 al empezar: PurgeStartedAt y un sello nuevo (aún no hay método de dominio).
    public static Task MarkPurgeStartedAsync(IServiceProvider services, Guid organizationId)
        => ChangeAsync(services, organizationId, (db, organization) =>
        {
            db.Entry(organization).Property(o => o.PurgeStartedAt).CurrentValue = DateTime.UtcNow;
            db.Entry(organization).Property(o => o.ConcurrencyStamp).CurrentValue = Guid.NewGuid();
        });

    // Restauración hecha por otra operación del mismo owner (D-45): regenera también su OwnershipStamp.
    public static async Task RestoreDirectlyAsync(IServiceProvider services, Guid organizationId, Guid ownerId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organization = await db.Organizations.IgnoreQueryFilters().SingleAsync(o => o.OrganizationId == organizationId);
        var owner = await db.Users.SingleAsync(u => u.UserId == ownerId);
        organization.Restore();
        owner.TouchOwnership();
        await db.SaveChangesAsync();
    }

    // Límite max_owned_orgs del plan para ese tipo de cuenta, creado si falta (una base por factory).
    public static async Task EnsureOwnedOrganizationLimitAsync(IServiceProvider services, string plan, string legalType, int? limit)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var billingPlan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Name == plan);
        if (billingPlan is null)
        {
            billingPlan = BillingPlan.Create(plan, plan, 0m, 0m);
            db.BillingPlans.Add(billingPlan);
        }
        var exists = await db.PlanLimits.AnyAsync(l =>
            l.PlanId == billingPlan.Id && l.LegalType == legalType && l.FeatureKey == PlanLimits.FeatureKeys.MaxOwnedOrgs);
        if (!exists)
            db.PlanLimits.Add(PlanLimit.Create(billingPlan.Id, legalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, limit));
        await db.SaveChangesAsync();
    }

    private static async Task ChangeAsync(
        IServiceProvider services, Guid organizationId, Action<ZentoryDbContext, Organization> change)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organization = await db.Organizations.IgnoreQueryFilters().SingleAsync(o => o.OrganizationId == organizationId);
        change(db, organization);
        await db.SaveChangesAsync();
    }
}

// Host con los límites organization-delete y organization-restore en sus valores por defecto (5 por hora por usuario,
// SR-0018.29) y los demás subidos, para probar solo esos contadores (PLAN-0018 CA-106).
public sealed class OrganizationRateLimitApiFactory : ZentoryApiFactory
{
    protected override bool RaiseAuthRateLimits => false;

    protected override IDictionary<string, string?> DefaultSettings
    {
        get
        {
            var settings = base.DefaultSettings;
            foreach (var (key, value) in RaisedAuthRateLimitSettings())
                if (!key.StartsWith(AuthRateLimitPolicies.OrganizationSection + ":", StringComparison.Ordinal))
                    settings[key] = value;
            return settings;
        }
    }
}

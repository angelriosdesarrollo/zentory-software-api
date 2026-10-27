using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T8: contrato de GET /me (hasPassword y hasGoogle, parte de CA-112, RF-81 h) y de GET /me/organizations
// (plan efectivo del owner y ownerName por membresía, contrato de CA-77, RF-69 a, RF-76 d, C-11), sobre el host real.
public sealed class ProfileTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ProfileEndpoint       = "/api/v1/me";
    private const string OrganizationsEndpoint = "/api/v1/me/organizations";

    // ── CA-112 (parte): métodos de acceso en GET /me ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true,  false)]
    [InlineData(false, true)]
    [InlineData(true,  true)]
    public async Task PLAN0018_CA112_GetMe_SegunMetodosDeLaCuenta_DevuelveHasPasswordYHasGoogle(bool withPassword, bool withGoogle)
    {
        var (user, org) = await SeedOwnerAsync(withPassword, withGoogle);
        using var http = ClientWithToken(factory.Tokens.ForUser(user, org, OrgRole.Owner, Plan.Free));

        using var response = await http.GetAsync(ProfileEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(withPassword, body.RootElement.GetProperty("hasPassword").GetBoolean());
        Assert.Equal(withGoogle, body.RootElement.GetProperty("hasGoogle").GetBoolean());
    }

    // ── CA-77 (contrato): plan del owner y ownerName en GET /me/organizations ──────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA77_MeOrganizations_MemberDeOrgStudioConPlanPropioFree_RecibePlanStudioYOwnerName()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Studio).WithMember(OrgRole.Member))
            .ForB(b => b.WithPlan(Plan.Pro))
            .BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        await SeedOwnSubscriptionAsync(member, tenants.A.Organization, Plan.Free);
        await tenants.SeedAsync(OrganizationMember.Create(tenants.B.OrganizationId, member.UserId, OrgRole.Member,
            invitedBy: tenants.B.Owner.UserId));
        using var http = tenants.ClientFor(tenants.A, member);

        using var response = await http.GetAsync(OrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var byOrg = body.RootElement.EnumerateArray()
            .ToDictionary(item => Guid.Parse(item.GetProperty("orgId").GetString()!), item => item.Clone());

        var inA = byOrg[tenants.A.OrganizationId];
        Assert.Equal(Plan.Studio, inA.GetProperty("plan").GetString());
        Assert.Equal(FullName(tenants.A.Owner), inA.GetProperty("ownerName").GetString());

        // El plan es el del owner de cada organización, no el del usuario (free) ni el de otra organización.
        var inB = byOrg[tenants.B.OrganizationId];
        Assert.Equal(Plan.Pro, inB.GetProperty("plan").GetString());
        Assert.Equal(FullName(tenants.B.Owner), inB.GetProperty("ownerName").GetString());
    }

    [Fact]
    public async Task PLAN0018_CA77_MeOrganizations_Owner_RecibeSuPlanYSuPropioNombreComoOwnerName()
    {
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithPlan(Plan.Pro)).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.GetAsync(OrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(Plan.Pro, item.GetProperty("plan").GetString());
        Assert.Equal(FullName(tenants.A.Owner), item.GetProperty("ownerName").GetString());
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static string FullName(User user) => $"{user.FirstName} {user.LastName}";

    private async Task<(User User, Organization Org)> SeedOwnerAsync(bool withPassword, bool withGoogle)
    {
        var org = Organization.Create($"Org perfil {Guid.NewGuid():N}", LegalType.Freelance);
        var user = User.Create($"perfil-{Guid.NewGuid():N}@zentory.test", "Perfil", "Prueba", OrgRole.Owner,
            passwordHash: withPassword ? BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), workFactor: 4) : null,
            googleId:     withGoogle ? $"google-{Guid.NewGuid():N}" : null,
            organizationId: org.OrganizationId);
        org.SetOwner(user.UserId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Organizations.Add(org);
        db.Users.Add(user);
        db.OrganizationMembers.Add(OrganizationMember.Create(org.OrganizationId, user.UserId, OrgRole.Owner));
        await db.SaveChangesAsync();
        return (user, org);
    }

    // Suscripción activa propia del usuario (como owner potencial), para comprobar que no se usa en organizaciones ajenas.
    private async Task SeedOwnSubscriptionAsync(User user, Organization billingOrganization, string plan)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var billingPlan = await db.BillingPlans.FirstOrDefaultAsync(p => p.Name == plan);
        if (billingPlan is null)
        {
            billingPlan = BillingPlan.Create(plan, plan, 0m, 0m);
            db.BillingPlans.Add(billingPlan);
        }

        var customer = BillingCustomer.Create(
            billingOrganization.OrganizationId, gatewayCustomerId: $"test_{user.UserId:N}", user.Email, user.FirstName);
        var subscription = Subscription.Create(user.UserId, customer.Id, billingPlan.Id);
        subscription.Activate($"test_{user.UserId:N}", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(30));
        db.BillingCustomers.Add(customer);
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

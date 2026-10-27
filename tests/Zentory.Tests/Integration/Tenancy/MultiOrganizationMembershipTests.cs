using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Tenancy;

// PLAN-0018 RF-12 (CA-16; ADR-0008 §3): con el filtro de tenant activo, OrganizationMember solo deja ver la
// organización del token. GET /me/organizations y POST /organization/switch cruzan organizaciones del mismo usuario por
// IUserMembershipReader, sobre el host real (JwtBearer + TenantContext + ZentoryDbContext). Cada test siembra sus
// propios datos con ids nuevos; la siembra corre sin HttpContext, sin filtro de tenant, como el seed real.
public sealed class MultiOrganizationMembershipTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OrganizationsEndpoint = "/api/v1/me/organizations";
    private const string SwitchEndpoint        = "/api/v1/organization/switch";

    [Fact]
    public async Task PLAN0018_CA16_MeOrganizations_UsuarioDeAyB_ConTokenDeA_ListaAyB()
    {
        var seed = await SeedUserInTwoOrganizationsAsync();
        var token = factory.Tokens.ForUser(seed.User, seed.OrgA, OrgRole.Owner, Plan.Free);

        using var response = await SendAsync(HttpMethod.Get, OrganizationsEndpoint, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var listed = body.RootElement.EnumerateArray()
            .Select(item => Guid.Parse(item.GetProperty("orgId").GetString()!))
            .Order()
            .ToList();
        Assert.Equal(new[] { seed.OrgA.OrganizationId, seed.OrgB.OrganizationId }.Order(), listed);
    }

    [Fact]
    public async Task PLAN0018_CA16_MeOrganizations_MembresiaBorradaEnC_NoLaLista()
    {
        var seed = await SeedUserInTwoOrganizationsAsync();
        var token = factory.Tokens.ForUser(seed.User, seed.OrgA, OrgRole.Owner, Plan.Free);

        using var response = await SendAsync(HttpMethod.Get, OrganizationsEndpoint, token);

        // IgnoreQueryFilters quita también el borrado lógico: el lector lo repite (DeletedAt == null).
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(seed.RemovedOrg.OrganizationId.ToString(), content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PLAN0018_CA16_SwitchHaciaB_ConTokenDeA_DevuelveTokenConActiveOrgIdB()
    {
        var seed = await SeedUserInTwoOrganizationsAsync();
        var token = factory.Tokens.ForUser(seed.User, seed.OrgA, OrgRole.Owner, Plan.Free);

        using var response = await SendAsync(HttpMethod.Post, SwitchEndpoint, token,
            new { orgId = seed.OrgB.OrganizationId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var issued = new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal(seed.OrgB.OrganizationId.ToString(), issued.Claims.Single(c => c.Type == "active_org_id").Value);
        Assert.Equal(OrgRole.Member, issued.Claims.Single(c => c.Type == "active_org_role").Value);
        var memberships = body.RootElement.GetProperty("memberships").EnumerateArray()
            .Select(item => Guid.Parse(item.GetProperty("orgId").GetString()!))
            .Order()
            .ToList();
        Assert.Equal(new[] { seed.OrgA.OrganizationId, seed.OrgB.OrganizationId }.Order(), memberships);
    }

    [Fact]
    public async Task PLAN0018_CA16_SwitchHaciaMembresiaBorrada_Devuelve404()
    {
        var seed = await SeedUserInTwoOrganizationsAsync();
        var token = factory.Tokens.ForUser(seed.User, seed.OrgA, OrgRole.Owner, Plan.Free);

        using var response = await SendAsync(HttpMethod.Post, SwitchEndpoint, token,
            new { orgId = seed.RemovedOrg.OrganizationId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA16_SwitchHaciaOrganizacionAjena_Devuelve404()
    {
        var seed = await SeedUserInTwoOrganizationsAsync();
        var token = factory.Tokens.ForUser(seed.User, seed.OrgA, OrgRole.Owner, Plan.Free);

        // El lector filtra por el UserId del que llama: la membresía de otro usuario en una organización ajena no basta.
        using var response = await SendAsync(HttpMethod.Post, SwitchEndpoint, token,
            new { orgId = seed.ForeignOrg.OrganizationId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Seed(User User, Organization OrgA, Organization OrgB, Organization RemovedOrg, Organization ForeignOrg);

    // Usuario owner de A, member de B y con la membresía de C dada de baja. D es de otro usuario.
    private async Task<Seed> SeedUserInTwoOrganizationsAsync()
    {
        var orgA       = Organization.Create($"Org A {Guid.NewGuid():N}", LegalType.Empresa);
        var orgB       = Organization.Create($"Org B {Guid.NewGuid():N}", LegalType.Freelance);
        var removedOrg = Organization.Create($"Org C {Guid.NewGuid():N}", LegalType.Empresa);
        var foreignOrg = Organization.Create($"Org D {Guid.NewGuid():N}", LegalType.Empresa);
        var user  = User.Create($"multi-{Guid.NewGuid():N}@zentory.test", "Ana", "Prueba", OrgRole.Owner,
            organizationId: orgA.OrganizationId);
        var other = User.Create($"other-{Guid.NewGuid():N}@zentory.test", "Beto", "Prueba", OrgRole.Owner,
            organizationId: orgB.OrganizationId);
        orgA.SetOwner(user.UserId);
        orgB.SetOwner(other.UserId);
        removedOrg.SetOwner(other.UserId);
        foreignOrg.SetOwner(other.UserId);

        var removed = OrganizationMember.Create(removedOrg.OrganizationId, user.UserId, OrgRole.Member);
        removed.SoftDelete();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Organizations.AddRange(orgA, orgB, removedOrg, foreignOrg);
        db.Users.AddRange(user, other);
        db.OrganizationMembers.AddRange(
            OrganizationMember.Create(orgA.OrganizationId, user.UserId, OrgRole.Owner),
            OrganizationMember.Create(orgB.OrganizationId, user.UserId, OrgRole.Member),
            OrganizationMember.Create(orgB.OrganizationId, other.UserId, OrgRole.Owner),
            OrganizationMember.Create(foreignOrg.OrganizationId, other.UserId, OrgRole.Owner),
            removed);
        await db.SaveChangesAsync();

        return new Seed(user, orgA, orgB, removedOrg, foreignOrg);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}

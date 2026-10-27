using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Middleware;
using Zentory.Application.CollaboratorPortal;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Tenancy;

// PLAN-0018 RF-14 y RF-16 (CA-19; CA-20 en parte, se cierra en T30c; ADR-0006 anotado; SR-0018.7): la membresía se
// revalida contra la base en cada petición del esquema de usuario, sobre el host real (JwtBearer +
// MembershipValidationMiddleware + TenantContext). Los miembros se siembran con TenantBuilder y sus tokens son reales
// (TestTokens); la baja de la membresía se hace fuera de la petición, como la haría el DELETE de miembros.
public sealed class MembershipRevocationTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ClientsEndpoint       = "/api/v1/clients";
    private const string OrganizationsEndpoint = "/api/v1/me/organizations";
    private const string SwitchEndpoint        = "/api/v1/organization/switch";
    private const string RefreshEndpoint       = "/api/v1/auth/refresh";
    private const string PortalInvoicesEndpoint = "/api/v1/collaborator-portal/payout-invoices";
    private const string ActiveOrgClaim        = "active_org_id";
    private const string RevokedCode           = MembershipValidationMiddleware.MembershipRevokedCode;

    // ── CA-20: membresía borrada con token vigente ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/clients")]
    [InlineData("/API/V1/CLIENTS")]
    [InlineData("/api/v1/organization/profile")]
    [InlineData("/api/v1/stats/invoices")]
    public async Task PLAN0018_CA20_MembresiaBorrada_TokenVigente_EndpointNoExcluido_Devuelve401MembershipRevoked(string path)
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Member).BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, member);

        // Control positivo: con la membresía vigente, el mismo token pasa.
        using (var before = await http.GetAsync(path))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await RemoveMembershipAsync(tenants.A.OrganizationId, member.UserId);

        using var after = await http.GetAsync(path);
        await AssertMembershipRevokedAsync(after);
    }

    [Fact]
    public async Task PLAN0018_CA20_MembresiaBorrada_PostNoExcluido_Devuelve401SinEjecutarLaAccion()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, admin);
        await RemoveMembershipAsync(tenants.A.OrganizationId, admin.UserId);
        var name = $"Cliente revocado {Guid.NewGuid():N}";

        using var response = await http.PostAsJsonAsync(ClientsEndpoint, new { name, contactName = "Contacto" });

        await AssertMembershipRevokedAsync(response);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.False(await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Name == name));
    }

    [Fact]
    public async Task PLAN0018_CA20_MembresiaBorrada_MeOrganizations_Responde200ConLasVigentes()
    {
        var (tenants, member) = await MemberOfAAndBAsync();
        using var http = tenants.ClientFor(tenants.A, member);
        await RemoveMembershipAsync(tenants.A.OrganizationId, member.UserId);

        using var response = await http.GetAsync(OrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var listed = body.RootElement.EnumerateArray()
            .Select(item => Guid.Parse(item.GetProperty("orgId").GetString()!))
            .ToList();
        Assert.Equal([tenants.B.OrganizationId], listed);
    }

    [Fact]
    public async Task PLAN0018_CA20_MembresiaBorrada_SwitchHaciaOtraOrganizacionPropia_DevuelveTokenDeEsa()
    {
        var (tenants, member) = await MemberOfAAndBAsync();
        using var http = tenants.ClientFor(tenants.A, member);
        await RemoveMembershipAsync(tenants.A.OrganizationId, member.UserId);

        using var response = await http.PostAsJsonAsync(SwitchEndpoint, new { orgId = tenants.B.OrganizationId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = await IssuedAccessTokenAsync(response);
        Assert.Equal(tenants.B.OrganizationId.ToString(), issued.Claims.Single(c => c.Type == ActiveOrgClaim).Value);

        // Y con el token nuevo, los endpoints no excluidos vuelven a responder.
        using var switched = factory.CreateClient();
        switched.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.RawData);
        using var clients = await switched.GetAsync(ClientsEndpoint);
        Assert.Equal(HttpStatusCode.OK, clients.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA20_TokenDeColaborador_EnElPortal_NoPasaPorLaComprobacionDeMembresia()
    {
        // El colaborador no tiene ninguna OrganizationMember: si el middleware lo comprobara, respondería 401.
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var email = $"colaborador-{Guid.NewGuid():N}@zentory.test";
        var collaborator = Collaborator.Create(tenants.A.OrganizationId, "Persona", "hourly_contractor", email: email);
        await tenants.SeedAsync(collaborator);
        var session = factory.Tokens.ForCollaborator(email, [collaborator.Id], collaborator.Id,
            tenants.A.OrganizationId, tenants.A.Organization.Name, CollaboratorAuthMethod.MagicLink);

        using var response = await SendAsync(HttpMethod.Get, PortalInvoicesEndpoint, session);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task PLAN0018_CA20_PrincipalDeUsuarioSinActiveOrgIdValido_RutaNoExcluida_Devuelve401(string? activeOrgId)
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var token = TestUserJwt.Sign(factory.Services, tenants.A.Owner, activeOrgId);

        using var response = await SendAsync(HttpMethod.Get, ClientsEndpoint, token);

        await AssertMembershipRevokedAsync(response);
    }

    [Fact]
    public async Task PLAN0018_CA20_PrincipalDeUsuarioSinActiveOrgId_RutaExcluida_Responde200()
    {
        // Control del anterior: el token es válido para el host y la exclusión por metadato deja pasar
        // GET /me/organizations, que solo necesita el sub (sesión sin organización de RF-85).
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var token = TestUserJwt.Sign(factory.Services, tenants.A.Owner, activeOrgId: null);

        using var response = await SendAsync(HttpMethod.Get, OrganizationsEndpoint, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA20_TenantContext_RolDeLaBase_NoDelClaim()
    {
        // Claim owner, base member: el rol de la petición es el de la base y no es owner.
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Member).BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        var tokenClaimingOwner = factory.Tokens.ForUser(member, tenants.A.Organization, OrgRole.Owner, tenants.A.Plan);
        string? role = null;
        bool? isOwner = null;

        using var probe = new MembershipProbe(factory, tenant => { role = tenant.ActiveOrgRole; isOwner = tenant.IsOwner; });
        using var response = await probe.GetAsync(ClientsEndpoint, tokenClaimingOwner);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrgRole.Member, role);
        Assert.False(isOwner);
    }

    [Fact]
    public void PLAN0018_CA20_TenantContext_SinMembresiaResuelta_NoRecurreAlClaim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ActiveOrgClaim, Guid.NewGuid().ToString()), new Claim("active_org_role", OrgRole.Owner)],
            authenticationType: "Bearer"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        var tenant = new TenantContext(accessor, new MembershipContext());

        Assert.Throws<InvalidOperationException>(() => tenant.ActiveOrgRole);
        Assert.False(tenant.IsOwner);
    }

    [Fact]
    public void PLAN0018_CA20_TenantContext_IsOwner_ExigeRolOwnerYOwnerIdDeLaOrganizacion()
    {
        var userId = Guid.NewGuid();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var owner = new MembershipContext();
        owner.Resolve(Guid.NewGuid(), userId, OrgRole.Owner, ownerUserId: userId);
        var ownerRoleOnly = new MembershipContext();
        ownerRoleOnly.Resolve(Guid.NewGuid(), userId, OrgRole.Owner, ownerUserId: Guid.NewGuid());

        Assert.True(new TenantContext(accessor, owner).IsOwner);
        Assert.Equal(OrgRole.Owner, new TenantContext(accessor, ownerRoleOnly).ActiveOrgRole);
        Assert.False(new TenantContext(accessor, ownerRoleOnly).IsOwner);
    }

    [Fact]
    public void PLAN0018_CA20_Host_ValidaScopes_UnServicioScopedNoSeResuelveDesdeLaRaiz()
    {
        // ValidateScopes = true en todo ambiente (SR-0018.40): el host de pruebas corre en Testing, no en Development.
        Assert.Throws<InvalidOperationException>(() => factory.Services.GetRequiredService<IMembershipContext>());

        using var scope = factory.Services.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IMembershipContext>().IsResolved);
    }

    // ── CA-19: switch y refresh hacia una organización sin membresía ────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA19_SwitchHaciaOrganizacionSinMembresia_Devuelve404()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.PostAsJsonAsync(SwitchEndpoint, new { orgId = tenants.B.OrganizationId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA19_RefreshConActiveOrgIdSinMembresia_EmiteTokenDeUnaOrganizacionPropia()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var refreshToken = await AuthSessionApi.SeedRefreshTokenAsync(factory.Services, tenants.A.Owner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(RefreshEndpoint,
            new { refreshToken, activeOrgId = tenants.B.OrganizationId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = await IssuedAccessTokenAsync(response);
        var activeOrgId = issued.Claims.Single(c => c.Type == ActiveOrgClaim).Value;
        Assert.NotEqual(tenants.B.OrganizationId.ToString(), activeOrgId);
        Assert.Equal(tenants.A.OrganizationId.ToString(), activeOrgId);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Miembro (member) de A que además es member de B: puede salir de A y seguir en B.
    private async Task<(TenantPair Tenants, User Member)> MemberOfAAndBAsync()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Member).BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        await tenants.SeedAsync(OrganizationMember.Create(tenants.B.OrganizationId, member.UserId, OrgRole.Member,
            invitedBy: tenants.B.Owner.UserId));
        return (tenants, member);
    }

    private async Task RemoveMembershipAsync(Guid organizationId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var membership = await db.OrganizationMembers
            .SingleAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.DeletedAt == null);
        membership.SoftDelete();
        await db.SaveChangesAsync();
    }

    private static async Task AssertMembershipRevokedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(RevokedCode, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task<JwtSecurityToken> IssuedAccessTokenAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("accessToken").GetString());
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    // Host derivado con un filtro de acción global que lee ITenantContext de la petición, después de que
    // MembershipValidationMiddleware poblara la membresía. Comparte la base InMemory de la factory.
    private sealed class MembershipProbe : IDisposable
    {
        private readonly WebApplicationFactory<Program> _host;

        public MembershipProbe(ZentoryApiFactory factory, Action<ITenantContext> observe)
            => _host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.Configure<MvcOptions>(options => options.Filters.Add(new ObservingFilter(observe)))));

        public async Task<HttpResponseMessage> GetAsync(string path, string token)
        {
            using var client = _host.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request);
        }

        public void Dispose() => _host.Dispose();
    }

    private sealed class ObservingFilter(Action<ITenantContext> observe) : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
            => observe(context.HttpContext.RequestServices.GetRequiredService<ITenantContext>());

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}

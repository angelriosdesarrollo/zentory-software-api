using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Middleware;
using Zentory.Application.CollaboratorPortal;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Tenancy;

// PLAN-0018 RF-11 (CA-15; H-08, SR-1.1, SR-0018.6; ADR-0008 §1-2 y §4): los filtros globales de ZentoryDbContext aíslan
// por la organización activa del contexto que consulta, no por la del primer contexto del proceso. Cada test usa su
// propio proveedor interno de EF, así la caché del modelo nace con el contexto que el test construye primero (anónimo
// o autenticado) y no con el de otro test. Las lecturas pasan por los filtros: aquí no hay IgnoreQueryFilters.
// El tenant es el TenantContext real de la API sobre un HttpContext con el principal de cada caso.
public sealed class TenantQueryFilterTests : IDisposable
{
    private const string ActiveOrgClaim   = "active_org_id";
    private const string CollaboratorType = "hourly_contractor";

    private readonly ServiceProvider _efServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    private readonly DbContextOptions<ZentoryDbContext> _options;

    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    public TenantQueryFilterTests()
    {
        _options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseInMemoryDatabase($"zentory-tenancy-{Guid.NewGuid():N}")
            .UseInternalServiceProvider(_efServices)
            .Options;
    }

    public void Dispose() => _efServices.Dispose();

    // ── CA-15: anónimo primero ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA15_ContextoAnonimoPrimero_UsuarioDeA_SoloRecibeFilasDeA()
    {
        // El primer contexto del proveedor es anónimo: con H-08 fijaba el filtro apagado para todos los demás.
        await using (var anonymous = UserContext(Anonymous()))
            Assert.Equal(0, await anonymous.Clients.CountAsync());
        await SeedBothOrganizationsAsync();

        await using var contextA = UserContext(UserOf(_orgA));
        await using var contextB = UserContext(UserOf(_orgB));

        await AssertOnlyRowsOfAsync(contextA, _orgA);
        await AssertOnlyRowsOfAsync(contextB, _orgB);
    }

    [Fact]
    public async Task PLAN0018_CA15_ContextoAnonimoPrimero_ContextosDeAyBIntercalados_CadaUnoVeSoloLoSuyo()
    {
        await using (var anonymous = UserContext(Anonymous()))
            Assert.Equal(0, await anonymous.Collaborators.CountAsync());
        await SeedBothOrganizationsAsync();

        await using var contextA = UserContext(UserOf(_orgA));
        await using var contextB = UserContext(UserOf(_orgB));

        // Mismo modelo cacheado y consultas alternadas: el filtro se evalúa por instancia en cada una.
        Assert.Equal([_orgA], await contextA.Clients.Select(c => c.OrganizationId).ToListAsync());
        Assert.Equal([_orgB], await contextB.Clients.Select(c => c.OrganizationId).ToListAsync());
        Assert.Equal([_orgA], await contextA.Invoices.Select(i => i.OrganizationId).ToListAsync());
        Assert.Equal([_orgB], await contextB.Invoices.Select(i => i.OrganizationId).ToListAsync());
    }

    // ── CA-15: autenticado primero ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA15_ContextoAutenticadoPrimero_ContextoSinHttpContext_NoLanzaYNoFiltraPorTenant()
    {
        await using (var first = UserContext(UserOf(_orgA)))
            Assert.Equal(0, await first.Clients.CountAsync());
        await SeedBothOrganizationsAsync();

        // Job de Hangfire, seed o CLI: TenantContext sin HttpContext. No lanza y ve las dos organizaciones.
        await using var withoutHttp = UserContext(httpContext: null);
        await AssertRowsOfBothAsync(withoutHttp);

        // Constructor sin tenant (fábrica de diseño, TestDbContextFactory).
        await using var withoutTenant = new ZentoryDbContext(_options);
        await AssertRowsOfBothAsync(withoutTenant);
    }

    [Fact]
    public async Task PLAN0018_CA15_ContextoAutenticadoPrimero_DespuesUsuarioDeB_SoloRecibeFilasDeB()
    {
        await using (var first = UserContext(UserOf(_orgA)))
            Assert.Equal(0, await first.Projects.CountAsync());
        await SeedBothOrganizationsAsync();

        await using var contextB = UserContext(UserOf(_orgB));

        await AssertOnlyRowsOfAsync(contextB, _orgB);
    }

    // ── CA-15: falla cerrado (SR-0018.6) ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task PLAN0018_CA15_PrincipalAutenticadoSinActiveOrgIdValido_ConsultaClients_RecibeCeroFilas(string? activeOrgId)
    {
        await SeedBothOrganizationsAsync();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (activeOrgId is not null)
            claims.Add(new Claim(ActiveOrgClaim, activeOrgId));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));

        await using var context = UserContext(principal);

        Assert.Empty(await context.Clients.ToListAsync());
        Assert.Equal(0, await context.Collaborators.CountAsync());
    }

    [Fact]
    public void PLAN0018_CA15_TenantContext_ActiveOrgIdNoGuid_AutenticadoSinOrganizacionActiva()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ActiveOrgClaim, "no-es-un-guid")], authenticationType: "Bearer"));

        var tenant = new TenantContext(new FixedHttpContextAccessor(new DefaultHttpContext { User = principal }), new MembershipContext());

        Assert.True(tenant.IsAuthenticated);
        Assert.False(tenant.HasActiveOrganization);
    }

    [Fact]
    public async Task PLAN0018_CA15_TenantContext_PrincipalCambiaTrasResolverElContexto_FiltraPorElPrincipalFinal()
    {
        await SeedBothOrganizationsAsync();
        var http = new DefaultHttpContext();
        var tenant = new TenantContext(new FixedHttpContextAccessor(http), new MembershipContext());
        await using var context = new ZentoryDbContext(_options, tenant);
        Assert.False(tenant.IsAuthenticated);

        // La autorización del esquema de colaborador reemplaza HttpContext.User después de que el DbContext (y su
        // AuditInterceptor) resolvieron el tenant: el filtro debe ver el principal final, no el del constructor.
        http.User = UserOf(_orgB);

        Assert.True(tenant.HasActiveOrganization);
        Assert.Equal([_orgB], await context.Clients.Select(c => c.OrganizationId).ToListAsync());
    }

    // ── CA-16 / ADR-0008 §4: sesión de colaborador ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA16_SesionDeColaboradorEnA_FiltroDeTenant_SoloVeColaboradoresYCuentasDeA()
    {
        var email = $"persona-{Guid.NewGuid():N}@zentory.test";
        var (inA, inB) = await SeedCollaboratorInBothAsync(email);

        await using var portal = UserContext(CollaboratorSessionIn(email, [inA.Id, inB.Id], inA.Id, _orgA));

        Assert.Equal([inA.Id], await portal.Collaborators.Where(c => c.Email == email).Select(c => c.Id).ToListAsync());
        Assert.Equal([_orgA], await portal.CollaboratorPayoutInvoices.Select(p => p.OrganizationId).ToListAsync());
        Assert.Null(await portal.CollaboratorPayoutInvoices.FirstOrDefaultAsync(p => p.CollaboratorId == inB.Id));
    }

    [Fact]
    public async Task PLAN0018_CA16_SesionDeColaboradorEnA_ListByEmailAsync_DevuelveAyBSinBorrados()
    {
        var email = $"persona-{Guid.NewGuid():N}@zentory.test";
        var (inA, inB) = await SeedCollaboratorInBothAsync(email);
        await using (var seed = new ZentoryDbContext(_options))
        {
            var removed = Collaborator.Create(_orgB, "Persona borrada", CollaboratorType, email: email);
            removed.SoftDelete();
            seed.Collaborators.Add(removed);
            await seed.SaveChangesAsync();
        }

        await using var portal = UserContext(CollaboratorSessionIn(email, [inA.Id, inB.Id], inA.Id, _orgA));
        var memberships = await new CollaboratorRepository(portal).ListByEmailAsync(email.ToUpperInvariant());

        // Cruza organizaciones (lista blanca de ADR-0008 §3) pero repite el borrado lógico a mano.
        Assert.Equal(new[] { inA.Id, inB.Id }.Order(), memberships.Select(m => m.Id).Order());
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private ZentoryDbContext UserContext(ClaimsPrincipal principal)
        => UserContext(new DefaultHttpContext { User = principal });

    private ZentoryDbContext UserContext(HttpContext? httpContext)
        => new(_options, new TenantContext(new FixedHttpContextAccessor(httpContext), new MembershipContext()));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal UserOf(Guid organizationId)
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ActiveOrgClaim, organizationId.ToString()),
                new Claim("active_org_role", OrgRole.Owner),
            ],
            authenticationType: "Bearer"));

    // Principal con los claims reales que emite CollaboratorJwtService (esquema CollaboratorScheme).
    private static ClaimsPrincipal CollaboratorSessionIn(
        string email, IReadOnlyList<Guid> collaboratorIds, Guid activeCollaboratorId, Guid organizationId)
    {
        var token = TestCollaboratorJwt.CreateService().GenerateSessionToken(
            email, collaboratorIds, activeCollaboratorId, organizationId, "Org activa", CollaboratorAuthMethod.MagicLink);
        return new ClaimsPrincipal(new ClaimsIdentity(TestCollaboratorJwt.Read(token).Claims, "CollaboratorScheme"));
    }

    // Una fila de cada tabla de CA-15 en A y otra en B, sembradas sin tenant (como el seed real).
    private async Task SeedBothOrganizationsAsync()
    {
        await using var seed = new ZentoryDbContext(_options);
        foreach (var organizationId in new[] { _orgA, _orgB })
        {
            var client  = Client.Create(organizationId, $"Cliente {organizationId:N}", "Contacto");
            var project = Project.Create(organizationId, client.Id, "Proyecto", BillingType.Hourly, 1000m);
            seed.Clients.Add(client);
            seed.Projects.Add(project);
            seed.Invoices.Add(Invoice.Create(organizationId, client.Id, $"F-{Guid.NewGuid():N}", new DateOnly(2026, 12, 31)));
            seed.Proposals.Add(Proposal.Create(organizationId, client.Id, "Propuesta"));
            seed.TimeEntries.Add(TimeEntry.Create(organizationId, project.Id, new DateOnly(2026, 10, 1), 2m, 50m));
            seed.Collaborators.Add(Collaborator.Create(organizationId, "Persona", CollaboratorType));
        }
        await seed.SaveChangesAsync();
    }

    private async Task<(Collaborator InA, Collaborator InB)> SeedCollaboratorInBothAsync(string email)
    {
        await using var seed = new ZentoryDbContext(_options);
        var inA = Collaborator.Create(_orgA, "Persona A", CollaboratorType, email: email);
        var inB = Collaborator.Create(_orgB, "Persona B", CollaboratorType, email: email);
        seed.Collaborators.AddRange(inA, inB);
        seed.CollaboratorPayoutInvoices.Add(CollaboratorPayoutInvoice.Create(_orgA, inA.Id, "2026-09", "Servicios", 100m));
        seed.CollaboratorPayoutInvoices.Add(CollaboratorPayoutInvoice.Create(_orgB, inB.Id, "2026-09", "Servicios", 100m));
        await seed.SaveChangesAsync();
        return (inA, inB);
    }

    private static async Task AssertOnlyRowsOfAsync(ZentoryDbContext context, Guid organizationId)
    {
        var organizationsSeen = new Dictionary<string, List<Guid>>
        {
            ["Clients"]       = await context.Clients.Select(e => e.OrganizationId).ToListAsync(),
            ["Projects"]      = await context.Projects.Select(e => e.OrganizationId).ToListAsync(),
            ["Invoices"]      = await context.Invoices.Select(e => e.OrganizationId).ToListAsync(),
            ["Proposals"]     = await context.Proposals.Select(e => e.OrganizationId).ToListAsync(),
            ["TimeEntries"]   = await context.TimeEntries.Select(e => e.OrganizationId).ToListAsync(),
            ["Collaborators"] = await context.Collaborators.Select(e => e.OrganizationId).ToListAsync(),
        };

        foreach (var (table, organizations) in organizationsSeen)
        {
            Assert.True(organizations.Count == 1, $"{table}: se esperaba 1 fila y hubo {organizations.Count}.");
            Assert.True(organizations[0] == organizationId, $"{table}: devolvió una fila de otra organización.");
        }
    }

    private async Task AssertRowsOfBothAsync(ZentoryDbContext context)
    {
        var expected = new[] { _orgA, _orgB }.Order().ToList();
        Assert.Equal(expected, (await context.Clients.Select(e => e.OrganizationId).ToListAsync()).Order());
        Assert.Equal(expected, (await context.Projects.Select(e => e.OrganizationId).ToListAsync()).Order());
        Assert.Equal(expected, (await context.Invoices.Select(e => e.OrganizationId).ToListAsync()).Order());
        Assert.Equal(expected, (await context.Proposals.Select(e => e.OrganizationId).ToListAsync()).Order());
        Assert.Equal(expected, (await context.TimeEntries.Select(e => e.OrganizationId).ToListAsync()).Order());
        Assert.Equal(expected, (await context.Collaborators.Select(e => e.OrganizationId).ToListAsync()).Order());
    }

    // HttpContextAccessor guarda el contexto en un AsyncLocal compartido por todos los accesores del mismo flujo: dos
    // tenants distintos en un mismo test necesitan cada uno su HttpContext fijo.
    private sealed class FixedHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}

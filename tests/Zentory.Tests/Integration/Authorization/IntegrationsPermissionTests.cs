using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Authorization;

// PLAN-0018 REV-0005 H-6 (RF-59 c, RF-66 c; FEATURE_PERMISSIONS §2.11): conectar y desconectar una integración es
// administración de la organización (RequireOrgAdmin). El member recibe 403 ACCESS_DENIED sin efecto; owner y admin
// conectan y desconectan. El GET del estado de conexión sigue abierto a cualquier miembro.
public sealed class IntegrationsPermissionTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string IntegrationsEndpoint = "/api/v1/integrations";

    [Fact]
    public async Task PLAN0018_REV5_H6_Member_ConectarYDesconectar_Responde403AccessDeniedSinEfecto()
    {
        var (tenants, integrationId) = await SeedAsync();
        using var owner  = tenants.ClientFor(tenants.A);
        using var member = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));
        using (var connected = await owner.PostAsync(ConnectUrl(integrationId), Body()))
            Assert.Equal(HttpStatusCode.OK, connected.StatusCode);

        using (var overwrite = await member.PostAsync(ConnectUrl(integrationId), Body("sobrescrita")))
            await ForbiddenAssert.IsAccessDeniedAsync(overwrite);
        using (var disconnect = await member.DeleteAsync(ConnectUrl(integrationId)))
            await ForbiddenAssert.IsAccessDeniedAsync(disconnect);

        var stored = await ConnectionAsync(tenants.A.OrganizationId, integrationId);
        Assert.True(stored.IsActive);
        Assert.NotEqual("sobrescrita", stored.ConnectedAs);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_REV5_H6_OwnerYAdmin_ConectanYDesconectan(string role)
    {
        var (tenants, integrationId) = await SeedAsync();
        var user = role == OrgRole.Owner ? tenants.A.Owner : tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, user);

        using (var connected = await http.PostAsync(ConnectUrl(integrationId), Body()))
            Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        Assert.True((await ConnectionAsync(tenants.A.OrganizationId, integrationId)).IsActive);

        using (var disconnected = await http.DeleteAsync(ConnectUrl(integrationId)))
            Assert.Equal(HttpStatusCode.NoContent, disconnected.StatusCode);
        Assert.False((await ConnectionAsync(tenants.A.OrganizationId, integrationId)).IsActive);
    }

    [Fact]
    public async Task PLAN0018_REV5_H6_Member_ListarIntegraciones_SigueRespondiendo200()
    {
        var (tenants, _) = await SeedAsync();
        using var member = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));

        using var response = await member.GetAsync(IntegrationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static string ConnectUrl(string integrationId) => $"{IntegrationsEndpoint}/{integrationId}/connect";

    // El controlador fija IntegrationId con la ruta, pero el binder del record lo exige en el cuerpo.
    private static StringContent Body(string connectedAs = "cuenta@zentory.test")
        => new($$"""{"integrationId":"de-la-ruta","connectedAs":"{{connectedAs}}"}""", System.Text.Encoding.UTF8, "application/json");

    private async Task<(TenantPair Tenants, string IntegrationId)> SeedAsync()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).WithMember(OrgRole.Member).BuildAsync();
        var integrationId = $"test-{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.IntegrationCatalog.Add(IntegrationCatalog.Create(integrationId, "Integración de prueba", "Solo para tests"));
        await db.SaveChangesAsync();
        return (tenants, integrationId);
    }

    private async Task<OrganizationIntegration> ConnectionAsync(Guid organizationId, string integrationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationIntegrations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(oi => oi.OrganizationId == organizationId && oi.IntegrationId == integrationId);
    }
}

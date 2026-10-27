using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zentory.API.Authorization;
using Zentory.API.Extensions;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Extensions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 RF-15 (CA-120; SR-0018.40): el contexto de membresía es por petición. Un host Production arranca con
// ValidateScopes, y peticiones concurrentes de organizaciones con planes distintos no se cruzan.
//
// Sin paralelismo: el host Production registra Hangfire, que fija JobStorage.Current global del proceso (R-6).
[Collection(NonParallelCollection.Name)]
public sealed class ValidateScopesTests
{
    private const string UnusedConnectionString = "Host=unused;Database=zentory_unused;Username=zentory";
    private const int Repetitions = 50;

    [Fact]
    public async Task PLAN0018_CA120_ProductionHost_HostUnusedHangfireApagado_ConValidateScopes_ArrancaSinErroresDeAmbito()
    {
        await using var host = new MinimalHostFactory(Environments.Production, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]             = "false",
            [DatabaseStartupSettings.ConnectionStringKey] = UnusedConnectionString,
            [HangfireSettings.ServerEnabledKey]           = "false",
            [DevSeedSettings.EnabledKey]                  = "false",
        });

        using var client = host.CreateClient();

        // La autorización resuelve sus handlers (los de rol y plan, scoped) dentro de la petición: sin errores de ámbito, el anónimo
        // recibe el 401 de siempre.
        using var response = await client.GetAsync("/api/v1/organization/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // ValidateScopes está activo en Production: un servicio scoped no se resuelve desde la raíz.
        var fromRoot = Record.Exception(() => host.Services.GetServices<IAuthorizationHandler>().ToList());
        Assert.IsType<InvalidOperationException>(fromRoot);

        using var scope = host.Services.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>().ToList();
        Assert.Contains(handlers, handler => handler is OrgRoleRequirementHandler);
        Assert.Contains(handlers, handler => handler is MinimumPlanHandler);
    }

    [Fact]
    public async Task PLAN0018_CA120_PeticionesConcurrentesProYStudio_RequiresStudio_CadaUnaObtieneSuPlan_50VecesSinCruces()
    {
        await using var factory = new AuthorizationProbeApiFactory();
        // El plan del claim y la suscripción del owner coinciden (TenantBuilder): el test sigue valiendo con T25.
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro))
            .ForB(b => b.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio))
            .BuildAsync();
        using var pro = tenants.ClientFor(tenants.A);
        using var studio = tenants.ClientFor(tenants.B);

        for (var i = 0; i < Repetitions; i++)
        {
            var proTask = pro.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath);
            var studioTask = studio.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath);
            await Task.WhenAll(proTask, studioTask);

            using var proResponse = proTask.Result;
            using var studioResponse = studioTask.Result;

            await ForbiddenAssert.IsPlanRequiredAsync(proResponse, Plan.Studio);
            Assert.Equal(HttpStatusCode.OK, studioResponse.StatusCode);
            var body = await studioResponse.Content.ReadFromJsonAsync<AuthorizationProbeApiFactory.ProbeResponse>();
            Assert.Equal(tenants.B.OrganizationId, body!.OrganizationId);
        }
    }
}

using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zentory.API.Authorization;
using Zentory.API.Middleware;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;
using FinancialVisibilityService = Zentory.Infrastructure.Services.FinancialVisibility;

namespace Zentory.Tests.Integration.FinancialVisibility;

// PLAN-0018 CA-90 (RF-66 e; D-29): IFinancialVisibility y la convención "para member, los campos monetarios se omiten del
// DTO; no se envían en cero".
//   - Unitarios: CanSeeAmounts es true para owner y admin, false para member y sin membresía resuelta; un DTO de prueba
//     serializado con las opciones JSON reales del host no trae sus propiedades monetarias para member, y el null de una
//     propiedad no monetaria sigue saliendo (el DefaultIgnoreCondition global no cambió).
//   - Integración: una acción de prueba con [AnyOrgMember] arma el DTO con IFinancialVisibility de la petición, sobre la
//     membresía leída de la base; y GET plan-limits, profile y settings responden 200 a owner, admin y member.
public sealed class FinancialVisibilityTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ProbePath = "/api/v1/financial-visibility-probe";

    private const decimal HourlyRate = 85_000m;
    private const decimal Total      = 1_250_000m;

    // ── Unitarios ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner, true)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Member, false)]
    public void PLAN0018_CA90_CanSeeAmounts_OwnerYAdminTrue_MemberFalse(string role, bool expected)
    {
        var visibility = new FinancialVisibilityService(Membership(role));

        Assert.Equal(expected, visibility.CanSeeAmounts);
        Assert.Equal(expected ? HourlyRate : (decimal?)null, visibility.Amount(HourlyRate));
    }

    [Fact]
    public void PLAN0018_CA90_CanSeeAmounts_SinMembresiaResuelta_FallaCerrado()
    {
        // Acción excluida, sesión de colaborador o fuera de una petición: no se ven montos (y no se lanza).
        var visibility = new FinancialVisibilityService(new MembershipContext());

        Assert.False(visibility.CanSeeAmounts);
        Assert.Null(visibility.Amount(Total));
    }

    [Fact]
    public void PLAN0018_CA90_CanSeeAmounts_RolDesconocido_False()
    {
        Assert.False(new FinancialVisibilityService(Membership("contador")).CanSeeAmounts);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public void PLAN0018_CA90_DtoDePrueba_SerializadoConLasOpcionesDelHost_ParaMemberNoTraeLasPropiedadesMonetarias(string role)
    {
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var dto = FinancialProbeDto.For(new FinancialVisibilityService(Membership(role)));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, options));

        AssertAmounts(json.RootElement, visible: role != OrgRole.Member);
    }

    [Fact]
    public void PLAN0018_CA90_DtoDePrueba_LasPropiedadesMonetariasSonNullablesConWhenWritingNull()
    {
        // La convención de D-29 a nivel de propiedad: B, C y D la copian en sus DTO.
        foreach (var name in new[] { nameof(FinancialProbeDto.HourlyRate), nameof(FinancialProbeDto.Total) })
        {
            var property = typeof(FinancialProbeDto).GetProperty(name)!;
            Assert.Equal(typeof(decimal?), property.PropertyType);
            Assert.Equal(JsonIgnoreCondition.WhenWritingNull, property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition);
        }

        Assert.Null(typeof(FinancialProbeDto).GetProperty(nameof(FinancialProbeDto.Note))!.GetCustomAttribute<JsonIgnoreAttribute>());
    }

    [Fact]
    public void PLAN0018_CA90_IFinancialVisibility_RegistradoScopedConLaImplementacionDeMembresia()
    {
        // Scoped como IMembershipContext: una instancia por petición, nunca compartida entre peticiones (SR-0018.40).
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        var instance = first.ServiceProvider.GetRequiredService<IFinancialVisibility>();

        Assert.IsType<FinancialVisibilityService>(instance);
        Assert.Same(instance, first.ServiceProvider.GetRequiredService<IFinancialVisibility>());
        Assert.NotSame(instance, second.ServiceProvider.GetRequiredService<IFinancialVisibility>());
    }

    // ── Integración ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA90_AccionDePrueba_ConLaMembresiaDeLaPeticion_MemberNoRecibeMontos(string role)
    {
        using var host = WithProbe();
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = OrganizationDeletionApi.ClientFor(host, tenants, tenants.A, Actor(tenants.A, role));

        using var response = await http.GetAsync(ProbePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertAmounts(json.RootElement, visible: role != OrgRole.Member);
    }

    [Fact]
    public async Task PLAN0018_CA90_AccionDePrueba_RolDegradadoEnLaBase_TokenDeAdmin_NoRecibeMontos()
    {
        // La visibilidad sale de la membresía leída en la petición, no del claim: el token dice admin, la base member.
        using var host = WithProbe();
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Admin))
            .BuildAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = OrganizationDeletionApi.ClientFor(host, tenants, tenants.A, admin);
        await MemberManagementApi.SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, admin.UserId, OrgRole.Member);

        using var response = await http.GetAsync(ProbePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertAmounts(json.RootElement, visible: false);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA90_PlanLimitsProfileYSettings_Responden200AOwnerAdminYMember(string role)
    {
        await DemoScenarioSeed.EnsureAsync(factory.Services);
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, role));

        foreach (var path in new[]
                 {
                     "/api/v1/organization/plan-limits",
                     "/api/v1/organization/profile",
                     "/api/v1/organization/settings",
                 })
        {
            using var response = await http.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} en GET {path}: {(int)response.StatusCode}");
        }
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA90_PlanYBillingHistory_Responden2xxSoloAlOwner(string role)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, role));

        foreach (var path in new[] { "/api/v1/organization/plan", "/api/v1/organization/billing-history" })
        {
            using var response = await http.GetAsync(path);
            if (role == OrgRole.Owner)
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            else
                await ForbiddenAssert.IsAccessDeniedAsync(response);
        }
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static MembershipContext Membership(string role)
    {
        var membership = new MembershipContext();
        membership.Resolve(Guid.NewGuid(), Guid.NewGuid(), role, ownerUserId: Guid.NewGuid(), Plan.Pro);
        return membership;
    }

    private static Zentory.Domain.Entities.User Actor(SeededTenant tenant, string role)
        => role == OrgRole.Owner ? tenant.Owner : tenant.Member(role);

    // Para quien ve montos, el JSON trae las dos propiedades monetarias con su valor; para member no las trae (ni en
    // cero ni en null). Lo no monetario sale igual para todos, también el null de note.
    private static void AssertAmounts(JsonElement root, bool visible)
    {
        Assert.Equal("Proyecto de prueba", root.GetProperty("name").GetString());
        Assert.Equal("COP", root.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("note").ValueKind);

        if (visible)
        {
            Assert.Equal(HourlyRate, root.GetProperty("hourlyRate").GetDecimal());
            Assert.Equal(Total, root.GetProperty("total").GetDecimal());
        }
        else
        {
            Assert.False(root.TryGetProperty("hourlyRate", out _), "member recibió hourlyRate");
            Assert.False(root.TryGetProperty("total", out _), "member recibió total");
        }
    }

    private WebApplicationFactory<Program> WithProbe()
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().ConfigureApplicationPartManager(manager =>
                manager.FeatureProviders.Add(new ProbeControllerProvider()))));

    // DTO de prueba con la convención de D-29: montos nullables con WhenWritingNull por propiedad; note es nullable sin
    // atributo y prueba que el null global se sigue serializando.
    public sealed record FinancialProbeDto(
        string Name,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? HourlyRate,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Total,
        string Currency,
        string? Note)
    {
        public static FinancialProbeDto For(IFinancialVisibility visibility)
            => new("Proyecto de prueba", visibility.Amount(FinancialVisibilityTests.HourlyRate), visibility.Amount(FinancialVisibilityTests.Total), "COP", Note: null);
    }

    // Anidada a propósito: MVC solo descubre controladores públicos de primer nivel, así que solo existe en el host al que
    // ProbeControllerProvider la agrega.
    [ApiController]
    [Authorize]
    [Route("api/v1/financial-visibility-probe")]
    public sealed class FinancialVisibilityProbeController : ControllerBase
    {
        [AnyOrgMember]
        [HttpGet]
        public IActionResult Get([FromServices] IFinancialVisibility visibility) => Ok(FinancialProbeDto.For(visibility));
    }

    private sealed class ProbeControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var probe = typeof(FinancialVisibilityProbeController).GetTypeInfo();
            if (!feature.Controllers.Contains(probe))
                feature.Controllers.Add(probe);
        }
    }
}

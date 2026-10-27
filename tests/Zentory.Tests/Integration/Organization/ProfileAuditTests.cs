using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zentory.Application.Organization.Commands;
using Zentory.Application.Organization.DTOs;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

// Namespace distinto de la carpeta, como ProfileSettingsTests: "Organization" taparía la entidad en el resto de Integration.
namespace Zentory.Tests.Integration.OrganizationProfile;

// PLAN-0018 REV-0005 H-7 (RF-89, SR-0018.45, D-46): PATCH /organization/profile, la ruta que usa la interfaz, deja
// ActivityLog con el valor anterior y el nuevo para las claves de OrgSettingKey.Audited (profile.nit, profile.legal_name),
// igual que PATCH /organization/settings. Sin cambio de valor no hay fila, y el valor no llega a los logs de aplicación.
// El PATCH es parcial: solo cambian las propiedades presentes en el cuerpo (el cliente lo tipa Partial<OrganizationProfile>).
public sealed class ProfileAuditTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string ProfileEndpoint = "/api/v1/organization/profile";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ZentoryApiFactory              _factory;
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public ProfileAuditTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs)));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_REV5_H7_Admin_CambiaNitYRazonSocialPorPerfil_DejaActivityLogConValorAnteriorYNuevo()
    {
        var (tenants, admin, http) = await SeedAsync();
        var orgId = tenants.A.OrganizationId;

        await PatchAsync(http, new { nit = "900-1", legalName = "Razón inicial" });
        await PatchAsync(http, new { nit = "900-2", legalName = "Razón nueva" });

        var rows = await AuditRowsAsync(orgId);
        Assert.All(rows, r =>
        {
            Assert.Equal(orgId, r.EntityId);
            Assert.Equal(admin.UserId, r.UserId);
        });
        AssertChange(rows, OrgSettingKey.ProfileNit,       previous: null,            current: "900-1");
        AssertChange(rows, OrgSettingKey.ProfileNit,       previous: "900-1",         current: "900-2");
        AssertChange(rows, OrgSettingKey.ProfileLegalName, previous: null,            current: "Razón inicial");
        AssertChange(rows, OrgSettingKey.ProfileLegalName, previous: "Razón inicial", current: "Razón nueva");
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public async Task PLAN0018_REV5_H7_PerfilSinCambioDeValorOConClavesNoAuditadas_NoDejaActivityLog()
    {
        var (tenants, _, http) = await SeedAsync();
        await PatchAsync(http, new { nit = "900-1" });
        var count = (await AuditRowsAsync(tenants.A.OrganizationId)).Count;

        // Mismo NIT y cambios en campos que no se auditan.
        await PatchAsync(http, new { nit = "900-1", phone = "3000000000", city = "Medellín" });

        Assert.Equal(count, (await AuditRowsAsync(tenants.A.OrganizationId)).Count);
    }

    [Fact]
    public async Task PLAN0018_REV5_H7_PerfilVaciaElNitConCadenaVacia_DejaActivityLogConElCambioAVacio()
    {
        var (tenants, _, http) = await SeedAsync();
        await PatchAsync(http, new { nit = "900-1" });

        await PatchAsync(http, new { nit = "" });

        AssertChange(await AuditRowsAsync(tenants.A.OrganizationId), OrgSettingKey.ProfileNit, previous: "900-1", current: "");
    }

    [Fact]
    public async Task PLAN0018_REV5_H7_PerfilParcial_SoloCambiaLasPropiedadesPresentes_YNoBorraElResto()
    {
        var (_, _, http) = await SeedAsync();
        await PatchAsync(http, new { nit = "900-1", legalName = "Razón", city = "Bogotá" });

        // Como el onboarding: { workType } solo.
        await PatchAsync(http, new { workType = WorkType.All[0] });

        var profile = (await http.GetFromJsonAsync<OrganizationProfileDto>(ProfileEndpoint, Json))!;
        Assert.Equal("900-1", profile.Nit);
        Assert.Equal("Razón", profile.LegalName);
        Assert.Equal("Bogotá", profile.City);
    }

    [Fact]
    public async Task PLAN0018_REV5_H7_ValorSensibleDelPerfil_NoApareceEnLosLogsDeLaAplicacion()
    {
        var (_, _, http) = await SeedAsync();
        var nit = $"NIT-SECRETO-{Guid.NewGuid():N}";
        var legalName = $"Razon-secreta-{Guid.NewGuid():N}";

        await PatchAsync(http, new { nit, legalName });
        await PatchAsync(http, new { nit = nit + "-2" });

        Assert.DoesNotContain(_logs.Entries, e =>
            e.Message.Contains(nit, StringComparison.Ordinal) || e.Message.Contains(legalName, StringComparison.Ordinal));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(TenantPair Tenants, Zentory.Domain.Entities.User Admin, HttpClient Http)> SeedAsync()
    {
        var tenants = await new TenantBuilder(_factory).WithMember(OrgRole.Admin).BuildAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        var http = AuthSessionApi.Bearer(_host, tenants.TokenFor(tenants.A, admin));
        return (tenants, admin, http);
    }

    private static async Task PatchAsync(HttpClient http, object body)
    {
        using var response = await http.PatchAsJsonAsync(ProfileEndpoint, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record AuditRow(Guid EntityId, Guid? UserId, string? Metadata);

    private static void AssertChange(IEnumerable<AuditRow> rows, string key, string? previous, string? current)
    {
        Assert.Single(rows, r =>
        {
            using var metadata = JsonDocument.Parse(r.Metadata!);
            var root = metadata.RootElement;
            return root.GetProperty("key").GetString() == key
                && root.GetProperty("previous").GetString() == previous
                && root.GetProperty("current").GetString() == current;
        });
    }

    private async Task<List<AuditRow>> AuditRowsAsync(Guid organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.ActivityLogs.IgnoreQueryFilters()
            .Where(l => l.OrganizationId == organizationId
                     && l.EntityType == UpdateOrganizationSettingsCommandHandler.SettingsEntityType)
            .Select(l => new AuditRow(l.EntityId, l.UserId, l.Metadata))
            .ToListAsync();
    }
}

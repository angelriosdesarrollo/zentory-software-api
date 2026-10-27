using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Organization.Commands;
using Zentory.Application.Organization.Queries;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

// Namespace distinto de la carpeta: "Organization" como namespace taparía la entidad en el resto de Integration.
namespace Zentory.Tests.Integration.OrganizationSettingsWhitelist;

// PLAN-0018 RF-89 (CA-121; SR-0018.45; D-46, C-15): lista blanca de PATCH /organization/settings. Solo claves de
// DefaultOrgSettings.All, las profile.* conocidas y las 8 bank.* de la pestaña "Cuenta bancaria"; máximo 100. Los
// cambios de profile.nit, profile.legal_name y bank.* dejan ActivityLog con el valor anterior y el nuevo. Las
// comprobaciones de base usan IgnoreQueryFilters() (advertencia H-08 de ZentoryApiFactory).
public sealed class SettingsWhitelistTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string SettingsEndpoint = "/api/v1/organization/settings";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ── Rechazo: clave fuera de la lista ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("unknown.key")]
    [InlineData("bank.iban")]          // bank.* fuera de las 8 de la pestaña
    [InlineData("profile.country")]    // profile.* que ningún comando guarda
    [InlineData("Bank.Name")]          // la lista es ordinal: otra capitalización es otra clave
    [InlineData("")]
    public async Task PLAN0018_CA121_Admin_PatchConClaveFueraDeLaLista_Devuelve422ValidationErrorYNoCambiaNingunaClave(string rejected)
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var admin = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));
        using (var seed = await admin.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?>
               {
                   [OrgSettingKey.ProposalValidity] = "15",
                   [OrgSettingKey.BankName]         = "Banco inicial",
               }))
            Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        var before = await SettingsSnapshotAsync(tenants.A.OrganizationId);
        var logsBefore = await AuditRowsAsync(tenants.A.OrganizationId);

        // Las válidas de la misma petición tampoco se aplican.
        using var response = await admin.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?>
        {
            [OrgSettingKey.ProposalValidity] = "45",
            [OrgSettingKey.BankName]         = "Banco cambiado",
            [OrgSettingKey.ProfileNit]       = "900-cambiado",
            [rejected]                       = "x",
        });

        await AssertValidationErrorAsync(response);
        Assert.Equal(before, await SettingsSnapshotAsync(tenants.A.OrganizationId));
        Assert.Equal(logsBefore.Count, (await AuditRowsAsync(tenants.A.OrganizationId)).Count);
    }

    // ── Rechazo: más de 100 claves ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA121_Admin_PatchCon101Claves_Devuelve422PorCantidadSinEfecto()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var admin = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));
        var before = await SettingsSnapshotAsync(tenants.A.OrganizationId);

        // Todas las claves permitidas primero; el resto hasta 101 no puede ser válido (la lista tiene menos de 100).
        var body = UpdateOrganizationSettingsCommandValidator.AllowedKeys.ToDictionary(k => k, _ => (string?)"1");
        for (var i = 0; body.Count < UpdateOrganizationSettingsCommandValidator.MaxKeys + 1; i++)
            body[$"extra.key_{i:D3}"] = "1";
        Assert.Equal(101, body.Count);

        using var response = await admin.PatchAsJsonAsync(SettingsEndpoint, body);

        var details = await AssertValidationErrorAsync(response);
        // Un solo error, el de cantidad: con más de 100 no se enumera cada clave.
        var detail = Assert.Single(details);
        Assert.Contains(UpdateOrganizationSettingsCommandValidator.MaxKeys.ToString(), detail.GetProperty("message").GetString());
        Assert.Equal(before, await SettingsSnapshotAsync(tenants.A.OrganizationId));
        Assert.Empty(await AuditRowsAsync(tenants.A.OrganizationId));
    }

    // ── Aceptación: pestaña "Cuenta bancaria" y el resto de pestañas ────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA121_Admin_PatchConLas8ClavesBankDeLaPestanaCuentaBancaria_Devuelve200YLasGuarda()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var admin = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));
        // El mismo cuerpo que arma app/(app)/settings/page.tsx al guardar la pestaña.
        var body = new Dictionary<string, string?>
        {
            ["bank.name"]         = "Bancolombia",
            ["bank.type"]         = "Ahorros",
            ["bank.number"]       = "000-000000-00",
            ["bank.holder"]       = "Titular de prueba",
            ["bank.document"]     = "900.000.000-0",
            ["bank.currency"]     = "COP",
            ["bank.usd_platform"] = "Plataforma USD",
            ["bank.usd_account"]  = "pagos@zentory.test",
        };
        Assert.Equal(OrgSettingKey.Bank.Order(), body.Keys.Order());

        using var response = await admin.PatchAsJsonAsync(SettingsEndpoint, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = (await response.Content.ReadFromJsonAsync<SettingsBody>(Json))!.Settings;
        foreach (var (key, value) in body)
        {
            Assert.Equal(value, returned[key]);
            Assert.Equal(value, await StoredValueAsync(tenants.A.OrganizationId, key));
        }
    }

    [Fact]
    public async Task PLAN0018_CA121_Admin_PatchConLasClavesDeDefaultsYPerfil_Devuelve200()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var admin = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));
        // Regional, condiciones y PILA (las demás pestañas que usan este endpoint) más el perfil.
        var body = DefaultOrgSettings.All.Keys.Concat(OrgSettingKey.Profile)
            .ToDictionary(k => k, k => (string?)$"valor {k}");

        using var response = await admin.PatchAsJsonAsync(SettingsEndpoint, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body[OrgSettingKey.PilaDueDay], await StoredValueAsync(tenants.A.OrganizationId, OrgSettingKey.PilaDueDay));
        Assert.Equal(body[OrgSettingKey.ProfileCity], await StoredValueAsync(tenants.A.OrganizationId, OrgSettingKey.ProfileCity));
    }

    // ── Auditoría: valor anterior y nuevo ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA121_Admin_CambiaNitLegalNameYUnaClaveBank_DejaActivityLogConValorAnteriorYNuevo()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var admin = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));
        var orgId = tenants.A.OrganizationId;

        using (var first = await admin.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?>
               {
                   [OrgSettingKey.ProfileNit]       = "900-1",
                   [OrgSettingKey.ProfileLegalName] = "Razón inicial",
                   [OrgSettingKey.BankNumber]       = "111",
               }))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using (var second = await admin.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?>
               {
                   [OrgSettingKey.ProfileNit]       = "900-2",
                   [OrgSettingKey.ProfileLegalName] = "Razón nueva",
                   [OrgSettingKey.BankNumber]       = "222",
               }))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var rows = await AuditRowsAsync(orgId);
        Assert.All(rows, r =>
        {
            Assert.Equal(orgId, r.EntityId);
            Assert.Equal(tenants.A.Member(OrgRole.Admin).UserId, r.UserId);
        });
        AssertChange(rows, OrgSettingKey.ProfileNit,       previous: null,            current: "900-1");
        AssertChange(rows, OrgSettingKey.ProfileNit,       previous: "900-1",         current: "900-2");
        AssertChange(rows, OrgSettingKey.ProfileLegalName, previous: null,            current: "Razón inicial");
        AssertChange(rows, OrgSettingKey.ProfileLegalName, previous: "Razón inicial", current: "Razón nueva");
        AssertChange(rows, OrgSettingKey.BankNumber,       previous: null,            current: "111");
        AssertChange(rows, OrgSettingKey.BankNumber,       previous: "111",           current: "222");
        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public async Task PLAN0018_CA121_Admin_PatchSinCambioDeValorOClaveNoAuditada_NoDejaActivityLog()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var admin = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));
        var body = new Dictionary<string, string?> { [OrgSettingKey.BankHolder] = "Titular" };
        using (await admin.PatchAsJsonAsync(SettingsEndpoint, body)) { }
        var count = (await AuditRowsAsync(tenants.A.OrganizationId)).Count;

        // Mismo valor en una clave auditada y cambio en claves que no se auditan.
        body[OrgSettingKey.Language]     = "en";
        body[OrgSettingKey.ProfileEmail] = "otro@zentory.test";
        using (var response = await admin.PatchAsJsonAsync(SettingsEndpoint, body))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(count, (await AuditRowsAsync(tenants.A.OrganizationId)).Count);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed record SettingsBody(Dictionary<string, string?> Settings);

    private sealed record AuditRow(Guid EntityId, Guid? UserId, string? Metadata);

    private static async Task<List<JsonElement>> AssertValidationErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        return body.RootElement.GetProperty("details").EnumerateArray().Select(e => e.Clone()).ToList();
    }

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
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.ActivityLogs.IgnoreQueryFilters()
            .Where(l => l.OrganizationId == organizationId
                     && l.EntityType == UpdateOrganizationSettingsCommandHandler.SettingsEntityType)
            .Select(l => new AuditRow(l.EntityId, l.UserId, l.Metadata))
            .ToListAsync();
    }

    private async Task<string?> StoredValueAsync(Guid organizationId, string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && s.Key == key)
            .Select(s => s.Value)
            .SingleOrDefaultAsync();
    }

    // Instantánea comparable de todas las filas de settings de una organización.
    private async Task<string> SettingsSnapshotAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var rows = await db.OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId)
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value, s.UpdatedAt })
            .ToListAsync();
        return JsonSerializer.Serialize(rows);
    }
}

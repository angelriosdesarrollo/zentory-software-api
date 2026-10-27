using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Organization.DTOs;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

// Namespace distinto de la carpeta: "Organization" como namespace taparía la entidad en el resto de Integration.
namespace Zentory.Tests.Integration.OrganizationProfile;

// PLAN-0018 RF-15 (CA-21, decisión del humano 1; CA-22): perfil y settings de la organización. Escribir exige
// RequireOrgAdmin (owner o admin); leer, cualquier miembro vigente. Las comprobaciones de base usan IgnoreQueryFilters()
// (advertencia H-08 de ZentoryApiFactory).
public sealed class ProfileSettingsTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ProfileEndpoint  = "/api/v1/organization/profile";
    private const string SettingsEndpoint = "/api/v1/organization/settings";
    private const string SettingKey       = OrgSettingKey.ProposalValidity;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ── CA-21 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA21_OwnerYAdmin_PatchProfileYSettings_Devuelven200YElValorCambia(string role)
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        var user = role == OrgRole.Owner ? tenants.A.Owner : tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, user);
        var legalName = $"Razón social {role} {Guid.NewGuid():N}";
        var validity = NewSettingValue();

        using (var profile = await http.PatchAsJsonAsync(ProfileEndpoint, new { legalName }))
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using (var settings = await http.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?> { [SettingKey] = validity }))
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        Assert.Equal(legalName, (await GetProfileAsync(http)).LegalName);
        Assert.Equal(validity, (await GetSettingsAsync(http)).Settings[SettingKey]);
        Assert.Equal(legalName, await StoredValueAsync(tenants.A.OrganizationId, "profile.legal_name"));
        Assert.Equal(validity, await StoredValueAsync(tenants.A.OrganizationId, SettingKey));
    }

    [Fact]
    public async Task PLAN0018_CA21_Member_PatchProfileYSettings_Devuelve403AccessDeniedSinCambiarValores()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Member).BuildAsync();
        using var owner = tenants.ClientFor(tenants.A);
        using var member = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));
        var legalName = $"Razón social del owner {Guid.NewGuid():N}";
        var validity = NewSettingValue();
        using (await owner.PatchAsJsonAsync(ProfileEndpoint, new { legalName })) { }
        using (await owner.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?> { [SettingKey] = validity })) { }
        var before = await SettingsSnapshotAsync(tenants.A.OrganizationId);

        using (var profile = await member.PatchAsJsonAsync(ProfileEndpoint, new { legalName = "Cambio del member" }))
            await ForbiddenAssert.IsAccessDeniedAsync(profile);
        using (var settings = await member.PatchAsJsonAsync(SettingsEndpoint,
                   new Dictionary<string, string?> { [SettingKey] = "1", [OrgSettingKey.Language] = "en" }))
            await ForbiddenAssert.IsAccessDeniedAsync(settings);

        Assert.Equal(before, await SettingsSnapshotAsync(tenants.A.OrganizationId));
        Assert.Equal(legalName, (await GetProfileAsync(owner)).LegalName);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA21_OwnerAdminYMember_GetProfileYSettings_Devuelven200(string role)
    {
        var tenants = await new TenantBuilder(factory)
            .WithMember(OrgRole.Admin)
            .WithMember(OrgRole.Member)
            .BuildAsync();
        var user = role == OrgRole.Owner ? tenants.A.Owner : tenants.A.Member(role);
        using var http = tenants.ClientFor(tenants.A, user);

        using (var profile = await http.GetAsync(ProfileEndpoint))
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using (var settings = await http.GetAsync(SettingsEndpoint))
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
    }

    // ── CA-22 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA22_OwnerDeA_LeeYActualizaProfileYSettings_SoloVeYModificaLosDeA_BQuedaIgual()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        using var ownerA = tenants.ClientFor(tenants.A);
        using var ownerB = tenants.ClientFor(tenants.B);

        var legalNameA = $"Razón A {Guid.NewGuid():N}";
        var legalNameB = $"Razón B {Guid.NewGuid():N}";
        var validityA = "11";
        var validityB = "22";
        using (await ownerA.PatchAsJsonAsync(ProfileEndpoint, new { legalName = legalNameA })) { }
        using (await ownerA.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?> { [SettingKey] = validityA })) { }
        using (await ownerB.PatchAsJsonAsync(ProfileEndpoint, new { legalName = legalNameB, nit = "900-B" })) { }
        using (await ownerB.PatchAsJsonAsync(SettingsEndpoint, new Dictionary<string, string?> { [SettingKey] = validityB })) { }
        var snapshotB = await SettingsSnapshotAsync(tenants.B.OrganizationId);
        var organizationB = await OrganizationSnapshotAsync(tenants.B.OrganizationId);

        // Lectura: solo los de A.
        var profileA = await GetProfileAsync(ownerA);
        Assert.Equal(tenants.A.OrganizationId, profileA.Id);
        Assert.Equal(legalNameA, profileA.LegalName);
        Assert.Null(profileA.Nit);
        Assert.Equal(validityA, (await GetSettingsAsync(ownerA)).Settings[SettingKey]);

        // Escritura: solo cambia A.
        var updatedName = $"Razón A actualizada {Guid.NewGuid():N}";
        using (var profile = await ownerA.PatchAsJsonAsync(ProfileEndpoint, new { legalName = updatedName, nit = "900-A" }))
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using (var settings = await ownerA.PatchAsJsonAsync(SettingsEndpoint,
                   new Dictionary<string, string?> { [SettingKey] = "33", [OrgSettingKey.Language] = "en" }))
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        Assert.Equal(updatedName, await StoredValueAsync(tenants.A.OrganizationId, "profile.legal_name"));
        Assert.Equal(snapshotB, await SettingsSnapshotAsync(tenants.B.OrganizationId));
        Assert.Equal(organizationB, await OrganizationSnapshotAsync(tenants.B.OrganizationId));
        Assert.Equal(legalNameB, (await GetProfileAsync(ownerB)).LegalName);
        Assert.Equal(validityB, (await GetSettingsAsync(ownerB)).Settings[SettingKey]);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────────────────────

    private static string NewSettingValue() => Random.Shared.Next(31, 365).ToString();

    private static async Task<OrganizationProfileDto> GetProfileAsync(HttpClient http)
        => (await http.GetFromJsonAsync<OrganizationProfileDto>(ProfileEndpoint, Json))!;

    private static async Task<OrganizationSettingsDto> GetSettingsAsync(HttpClient http)
        => (await http.GetFromJsonAsync<OrganizationSettingsDto>(SettingsEndpoint, Json))!;

    private async Task<string?> StoredValueAsync(Guid organizationId, string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && s.Key == key)
            .Select(s => s.Value)
            .SingleOrDefaultAsync();
    }

    // Instantánea comparable de todas las filas de settings de una organización (perfil incluido).
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

    private async Task<string> OrganizationSnapshotAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organization = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(o => o.OrganizationId == organizationId);
        return JsonSerializer.Serialize(new { organization.Name, organization.LegalType, organization.OwnerId });
    }
}

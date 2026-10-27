using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zentory.API.Middleware;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T11b, CA-T11B-1 (parte de CA-118; RF-85, RF-61; D-44; SR-0018.38) sobre el host real: el owner free cuya
// única organización A eliminó entra por login, Google o refresh con una sesión sin organización activa, que solo sirve en
// las rutas con [SkipMembershipValidation] de la lista cerrada; desde ahí restaura A y vuelve a ella con switch. Sin
// organizaciones restaurables (fuera de plazo o con la purga iniciada) recibe el 4xx genérico de RF-61 (422 en login y
// Google; en refresh, el 401 REFRESH_TOKEN_INVALID de un token desconocido).
public sealed class SessionWithoutOrganizationTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword   = "Owner-Sin-Organizacion-2026!";
    private const string ClientsEndpoint = "/api/v1/clients";
    private const int    PurgeAfterDays  = 30;

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public SessionWithoutOrganizationTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = WithGoogle(factory, _google);
    }

    public void Dispose() => _host.Dispose();

    // ── Emisión: login, Google y refresh ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA118_LoginOwnerFreeConUnicaOrganizacionEliminada_Responde200SinActiveOrgIdNiMembresias()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = _host.CreateClient();

        using var response = await LoginAsync(http, owner.Owner.Email, OwnerPassword);

        AssertSessionWithoutOrganization(await ReadSessionAsync(response));
    }

    [Fact]
    public async Task PLAN0018_CA118_GoogleOwnerFreeConUnicaOrganizacionEliminada_Responde200SinActiveOrgIdNiMembresias()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = _host.CreateClient();

        using var response = await GoogleAsync(http, _google, owner.Owner.GoogleId!, owner.Owner);

        AssertSessionWithoutOrganization(await ReadSessionAsync(response));
    }

    [Fact]
    public async Task PLAN0018_CA118_RefreshOwnerFreeConUnicaOrganizacionEliminada_Responde200SinActiveOrgIdAunquePidaA()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, owner.Owner);
        using var http = _host.CreateClient();

        // Pide la eliminada: nunca recibe un token de ella (RF-16, SR-0018.26 d).
        using var response = await RefreshAsync(http, refreshToken, owner.OrganizationId);

        AssertSessionWithoutOrganization(await ReadSessionAsync(response));
    }

    [Fact]
    public async Task PLAN0018_CA118_RefreshConElRefreshDeLaSesionSinOrganizacion_EmiteOtraSesionSinOrganizacion()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = _host.CreateClient();
        using var login = await LoginAsync(http, owner.Owner.Email, OwnerPassword);
        var first = await ReadSessionAsync(login);

        using var response = await RefreshAsync(http, first.RefreshToken);

        var second = await ReadSessionAsync(response);
        AssertSessionWithoutOrganization(second);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
    }

    // ── Rutas útiles con la sesión sin organización ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA118_SesionSinOrganizacion_GetMeResponde200ConActiveOrgIdNull()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = Bearer(_host, await LoginWithoutOrganizationAsync(owner));

        using var response = await http.GetAsync(ProfileEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(owner.Owner.UserId, body.RootElement.GetProperty("id").GetGuid());
        foreach (var field in new[] { "activeOrgId", "activeOrgName", "activeOrgRole", "plan", "legalType" })
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty(field).ValueKind);
    }

    [Fact]
    public async Task PLAN0018_CA118_SesionSinOrganizacion_GetMeOrganizationsResponde200Vacio()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = Bearer(_host, await LoginWithoutOrganizationAsync(owner));

        using var response = await http.GetAsync(MyOrgsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task PLAN0018_CA118_SesionSinOrganizacion_DeletedListaAYRestoreResponde204()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = Bearer(_host, await LoginWithoutOrganizationAsync(owner));

        using (var deleted = await http.GetAsync(OrganizationRestoreApi.DeletedEndpoint))
        {
            Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
            using var body = JsonDocument.Parse(await deleted.Content.ReadAsStringAsync());
            var item = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal(owner.OrganizationId, item.GetProperty("id").GetGuid());
        }

        using var restore = await OrganizationRestoreApi.RestoreAsync(http, owner.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
    }

    [Theory]
    [InlineData(ClientsEndpoint)]
    [InlineData("/api/v1/organization/profile")]
    [InlineData("/api/v1/stats/invoices")]
    public async Task PLAN0018_CA118_SesionSinOrganizacion_RutaFueraDeLaListaCerrada_Responde401(string path)
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = Bearer(_host, await LoginWithoutOrganizationAsync(owner));

        using var response = await http.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(MembershipValidationMiddleware.MembershipRevokedCode, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PLAN0018_CA118_TrasRestaurar_SwitchHaciaAEmiteTokenConActiveOrgIdA()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        using var http = Bearer(_host, await LoginWithoutOrganizationAsync(owner));
        using (var restore = await OrganizationRestoreApi.RestoreAsync(http, owner.OrganizationId))
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        using var response = await SwitchAsync(http, owner.OrganizationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        Assert.Equal(owner.OrganizationId.ToString(), ClaimOf(accessToken, TestUserJwt.ActiveOrgClaim));

        // El token nuevo ya sirve en las rutas de la organización.
        using var withOrganization = Bearer(_host, accessToken);
        using var clients = await withOrganization.GetAsync(ClientsEndpoint);
        Assert.Equal(HttpStatusCode.OK, clients.StatusCode);
    }

    // ── Sin organizaciones restaurables: RF-61 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA118_OwnerConLaEliminadaFueraDePlazo_LoginGoogleYRefreshRespondenErrorGenerico()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync(DateTime.UtcNow.AddDays(-(PurgeAfterDays + 1)));

        await AssertNoSessionAsync(owner);
    }

    [Fact]
    public async Task PLAN0018_CA118_OwnerConLaEliminadaConPurgaIniciada_LoginGoogleYRefreshRespondenErrorGenerico()
    {
        var owner = await SeedOwnerWithDeletedOrganizationAsync();
        await OrganizationRestoreApi.MarkPurgeStartedAsync(_host.Services, owner.OrganizationId);

        await AssertNoSessionAsync(owner);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Owner free (con contraseña y Google) de A, su única organización, eliminada hace un día (o en la fecha pedida). El
    // límite max_owned_orgs de free admite una propia, así que restaurar A cabe.
    private async Task<SeededTenant> SeedOwnerWithDeletedOrganizationAsync(DateTime? deletedAtUtc = null)
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithPlan(Plan.Free).WithOwnerPassword(OwnerPassword).WithOwnerGoogleId($"google-{Guid.NewGuid():N}"))
            .BuildAsync();
        var a = tenants.A;
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Free, a.LegalType, 1);
        await OrganizationRestoreApi.MarkDeletedAsync(
            _host.Services, a.OrganizationId, a.Owner.UserId, deletedAtUtc ?? DateTime.UtcNow.AddDays(-1));
        return a;
    }

    private async Task<string> LoginWithoutOrganizationAsync(SeededTenant owner)
    {
        using var http = _host.CreateClient();
        using var response = await LoginAsync(http, owner.Owner.Email, OwnerPassword);
        var session = await ReadSessionAsync(response);
        AssertSessionWithoutOrganization(session);
        return session.RawAccessToken;
    }

    private async Task AssertNoSessionAsync(SeededTenant owner)
    {
        using var http = _host.CreateClient();

        using (var login = await LoginAsync(http, owner.Owner.Email, OwnerPassword))
            await AssertGenericErrorAsync(login, CredentialsField);

        using (var google = await GoogleAsync(http, _google, owner.Owner.GoogleId!, owner.Owner))
            await AssertGenericErrorAsync(google, CredentialsField);

        // Refresh: el 401 REFRESH_TOKEN_INVALID de un token desconocido (RF-30 c), con el mismo cuerpo.
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, owner.Owner);
        using var refresh = await RefreshAsync(http, refreshToken);
        using var unknown = await RefreshAsync(http, $"desconocido-{Guid.NewGuid():N}");
        await AssertRefreshTokenInvalidAsync(refresh);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await refresh.Content.ReadAsStringAsync());
    }

    private static void AssertSessionWithoutOrganization(IssuedSession session)
    {
        Assert.Null(session.ActiveOrgId);
        Assert.Equal(0, session.MembershipCount);
        Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        foreach (var claim in OrganizationClaims)
            Assert.Null(session.Claim(claim));
        Assert.NotNull(session.Claim("sub"));
    }
}

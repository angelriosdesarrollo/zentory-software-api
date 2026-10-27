using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 CA-T10B-1 (parte de CA-117; RF-84) y CA-T10B-2 (parte de CA-110; RF-81 h) sobre el host real:
// GET /auth/reauth-nonce emite el nonce del sub del JWT para la acción pedida, con reloj simulado; el arranque falla con
// Google:ClientId vacío o en blanco. El host deriva de la factory (reloj y logs propios) y comparte su base InMemory.
public sealed class ReauthNonceTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Endpoint = "/api/v1/auth/reauth-nonce";

    // Segundo exacto: exp viaja en segundos Unix.
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 30, 0, TimeSpan.Zero);

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeTimeProvider               _time = new(Now);
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public ReauthNonceTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_time);
            }));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA117_GetReauthNonce_ConJwt_Responde200ConNonceYExpiresAtEnCincoMinutos()
    {
        var (http, userId) = await OwnerClientAsync();

        using var response = await http.GetAsync($"{Endpoint}?action={ReauthAction.OrganizationDelete}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var nonce     = body.RootElement.GetProperty("nonce").GetString();
        var expiresAt = body.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.False(string.IsNullOrEmpty(nonce));
        Assert.Equal(Now + TimeSpan.FromMinutes(5), expiresAt);

        // El nonce emitido vale para el sub del JWT y esa acción, y para ninguna otra.
        var nonces = _host.Services.GetRequiredService<ReauthNonce>();
        Assert.True(nonces.TryValidate(nonce, userId, ReauthAction.OrganizationDelete, out var exp));
        Assert.Equal(expiresAt, exp);
        Assert.False(nonces.TryValidate(nonce, userId, ReauthAction.OrganizationTransfer, out _));
        Assert.False(nonces.TryValidate(nonce, Guid.NewGuid(), ReauthAction.OrganizationDelete, out _));
        // Emitir no consume ni registra nada (RF-84 a).
        Assert.False(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce!));
    }

    [Theory]
    [InlineData(ReauthAction.OrganizationTransfer)]
    [InlineData(ReauthAction.OrganizationTransferAccept)]
    public async Task PLAN0018_CA117_GetReauthNonce_LasTresAccionesDeRF81_Responden200(string action)
    {
        var (http, _) = await OwnerClientAsync();

        using var response = await http.GetAsync($"{Endpoint}?action={action}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("?action=organization.other-trap-value")]
    [InlineData("?action=Organization.Delete")]
    [InlineData("?action=")]
    [InlineData("")]
    public async Task PLAN0018_CA117_GetReauthNonce_AccionDesconocida_Responde422SinEco(string query)
    {
        var (http, _) = await OwnerClientAsync();

        using var response = await http.GetAsync(Endpoint + query);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("trap-value", text);
        Assert.DoesNotContain("Organization.Delete", text);
    }

    [Fact]
    public async Task PLAN0018_CA117_GetReauthNonce_SinJwt_Responde401()
    {
        using var http = _host.CreateClient();

        using var response = await http.GetAsync($"{Endpoint}?action={ReauthAction.OrganizationDelete}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA117_GetReauthNonce_LosEndpointsAnonimosDeAuthSiguenSinExigirJwt()
    {
        // [AllowAnonymous] pasó de la clase a cada acción: login sin Bearer sigue llegando al comando (422, no 401).
        using var http = _host.CreateClient();

        using var response = await http.PostAsync("/api/v1/auth/login",
            new StringContent("""{"email":"nobody@example.test","password":"nope-nope"}""", System.Text.Encoding.UTF8,
                "application/json"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA117_GetReauthNonce_CapturingLoggerProvider_NoRegistraElNonce()
    {
        var (http, _) = await OwnerClientAsync();
        var issued = new List<string>();

        foreach (var action in ReauthAction.All)
        {
            using var response = await http.GetAsync($"{Endpoint}?action={action}");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            issued.Add(body.RootElement.GetProperty("nonce").GetString()!);
        }

        Assert.NotEmpty(_logs.Entries);
        foreach (var nonce in issued)
            Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains(nonce, StringComparison.Ordinal));
    }

    // ── CA-110: el fallo de reautenticación por HTTP ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA110_ReauthenticationFailedException_PorElPipelineCompleto_Responde403ForbiddenUniforme()
    {
        // La excepción nace dentro de la acción (al leer el sub) y la traduce GlobalExceptionHandlingMiddleware del
        // host real: 403 y nunca 401, que el cliente trataría como sesión caducada (RF-81 d).
        await using var host = _host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContext>();
            services.AddScoped<ITenantContext, ReauthFailingTenantContext>();
        }));
        var tenants = await new TenantBuilder(_factory).BuildAsync();
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenants.TokenFor(tenants.A));

        using var response = await http.GetAsync($"{Endpoint}?action={ReauthAction.OrganizationDelete}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("forbidden", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("REAUTHENTICATION_FAILED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(new ReauthenticationFailedException().Message, body.RootElement.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("details").ValueKind);
    }

    // ── CA-110: arranque con Google:ClientId vacío ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PLAN0018_CA110_Host_ConGoogleClientIdVacioOEnBlanco_NoArranca(string clientId)
    {
        await using var factory = new MinimalHostFactory(AppEnvironments.Testing, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]     = "true",
            [DatabaseSettings.InMemoryNameKey]    = $"zentory-google-client-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]   = "false",
            [DevSeedSettings.EnabledKey]          = "false",
            [TestJwtSettings.GoogleClientIdSetting] = clientId,
        });

        var startupError = Record.Exception(() => factory.CreateClient());

        Assert.True(HasStartupError(startupError), $"El arranque no falló por Google:ClientId: {startupError?.GetType().Name}");
    }

    [Fact]
    public async Task PLAN0018_CA110_Host_ConGoogleClientId_Arranca()
    {
        // Control del anterior: la misma configuración con ClientId arranca.
        await using var factory = new MinimalHostFactory(AppEnvironments.Testing, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-google-client-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
        });

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────


    private async Task<(HttpClient Http, Guid UserId)> OwnerClientAsync()
    {
        var tenants = await new TenantBuilder(_factory).BuildAsync();
        var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenants.TokenFor(tenants.A));
        return (http, tenants.A.Owner.UserId);
    }

    // ITenantContext cuyo sub lanza el fallo de reautenticación: lo lee la acción, dentro del pipeline MVC.
    private sealed class ReauthFailingTenantContext : ITenantContext
    {
        public bool   IsAuthenticated       => true;
        public bool   HasActiveOrganization => false;
        public Guid   OrganizationId        => throw new InvalidOperationException();
        public Guid   UserId                => throw new ReauthenticationFailedException();
        public Guid   SessionId             => throw new InvalidOperationException();
        public string UserInitials          => "??";
        public string Plan                  => throw new InvalidOperationException();
        public string LegalType             => throw new InvalidOperationException();
        public string ActiveOrgRole         => throw new InvalidOperationException();
        public bool   IsOwner               => false;
    }

    private static bool HasStartupError(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException
                && current.Message.Contains(GoogleTokenValidator.ClientIdSetting, StringComparison.Ordinal))
                return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(HasStartupError))
                return true;
        }
        return false;
    }
}

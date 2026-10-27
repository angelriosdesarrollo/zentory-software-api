using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 CA-82 (RF-62; SR-0018.17, SR-0018.43; decisión del humano 4; D-38): ningún appsettings versionado trae
// dominio en App:BaseUrl ni en Cors:AllowedOrigins; fuera de Development y Testing las dos claves son obligatorias,
// https, absolutas, sin ruta y sin comodín, y el error nombra la clave sin el valor. En Development los enlaces usan
// http://localhost:3000 y CORS admite localhost en cualquier puerto, reconocido por host y no por prefijo. La política
// nunca emite Access-Control-Allow-Credentials.
//
// Sin paralelismo: el host Production registra Hangfire, que fija JobStorage.Current global del proceso (R-6).
[Collection(NonParallelCollection.Name)]
public sealed class AppUrlSettingsTests
{
    private const string UnusedConnectionString = "Host=unused;Database=zentory_unused;Username=zentory";
    private const string PreflightPath          = "/api/v1/auth/login";

    private static readonly string[] VersionedSettingsFiles =
        ["src/Zentory.API/appsettings.json", "src/Zentory.API/appsettings.Production.json"];

    // ── Archivos versionados ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA82_AppsettingsVersionados_SinDominioEnBaseUrlNiEnAllowedOrigins()
    {
        foreach (var file in VersionedSettingsFiles)
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Path, file)));

            foreach (var key in new[] { AppUrlSettingsValidation.BaseUrlSetting, AppUrlSettingsValidation.AllowedOriginsSetting })
            {
                var value = ValueOf(settings.RootElement, key);
                Assert.True(string.IsNullOrWhiteSpace(value), $"{file}: {key} trae un valor por defecto.");
            }
        }
    }

    [Fact]
    public void PLAN0018_CA82_CodigoDelApi_SinDominioDeZentoryQuemado()
    {
        // Los enlaces salen de IApplicationSettings.BaseUrl: ningún .cs de src/ arma una URL con el dominio del producto.
        var forbidden = new[] { "app.zentory", "zentory.co/", "zentory.io" };
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return forbidden.Any(domain => text.Contains(domain, StringComparison.OrdinalIgnoreCase));
            })
            .Select(path => Path.GetRelativePath(RepoRoot.Path, path))
            .ToList();

        Assert.Empty(offenders);
    }

    // ── Production: obligatorias y https ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA82_ProductionHost_ConLasDosClavesHttps_ArrancaYLasUsa()
    {
        await using var factory = ProductionHost("https://app.example.test/", "https://app.example.test");

        using var client = factory.CreateClient();

        Assert.Equal("https://app.example.test", factory.Services.GetRequiredService<IApplicationSettings>().BaseUrl);
        Assert.Equal(["https://app.example.test"], FrontendPolicy(factory.Services).Origins);
    }

    public static TheoryData<string, string?, string?, string> InvalidProductionSettings => new()
    {
        // caso,                          App:BaseUrl,                      Cors:AllowedOrigins,                         clave que debe nombrar el error
        { "base-url-falta",               null,                             "https://app.example.test",                  AppUrlSettingsValidation.BaseUrlSetting },
        { "base-url-vacia",               "",                               "https://app.example.test",                  AppUrlSettingsValidation.BaseUrlSetting },
        { "base-url-http",                "http://app.example.test",        "https://app.example.test",                  AppUrlSettingsValidation.BaseUrlSetting },
        { "base-url-relativa",            "app.example.test",               "https://app.example.test",                  AppUrlSettingsValidation.BaseUrlSetting },
        { "base-url-con-ruta",            "https://app.example.test/app",   "https://app.example.test",                  AppUrlSettingsValidation.BaseUrlSetting },
        { "base-url-comodin",             "https://*.example.test",         "https://app.example.test",                  AppUrlSettingsValidation.BaseUrlSetting },
        { "origenes-faltan",              "https://app.example.test",       null,                                        AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origenes-cadena-vacia",        "https://app.example.test",       "",                                          AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origenes-solo-comas",          "https://app.example.test",       " , ,",                                      AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origen-asterisco",             "https://app.example.test",       "*",                                         AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origen-subdominio-comodin",    "https://app.example.test",       "https://*.example.test",                    AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origen-http",                  "https://app.example.test",       "https://app.example.test,http://b.example.test", AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origen-null",                  "https://app.example.test",       "null",                                      AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origen-con-ruta",              "https://app.example.test",       "https://app.example.test/login",            AppUrlSettingsValidation.AllowedOriginsSetting },
        { "origen-relativo",              "https://app.example.test",       "app.example.test",                          AppUrlSettingsValidation.AllowedOriginsSetting },
    };

    [Theory]
    [MemberData(nameof(InvalidProductionSettings))]
    public async Task PLAN0018_CA82_ProductionHost_SinClaveOConValorNoHttpsAbsolutoSinRuta_NoArrancaYNombraLaClaveSinElValor(
        string scenario, string? baseUrl, string? allowedOrigins, string failingSetting)
    {
        await using var factory = ProductionHost(baseUrl, allowedOrigins);

        var error = Record.Exception(() => factory.CreateClient());

        Assert.True(error is not null, $"Caso {scenario}: el host Production arrancó con una configuración inválida.");
        var validation = FindInChain(error!, e => e.Message.Contains(failingSetting, StringComparison.Ordinal));
        Assert.True(validation is not null, $"Caso {scenario}: el error de arranque no nombra {failingSetting}.");

        foreach (var value in new[] { baseUrl, allowedOrigins }.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            Assert.False(validation!.Message.Contains(value!.Trim(), StringComparison.OrdinalIgnoreCase),
                $"Caso {scenario}: el mensaje de arranque contiene un valor configurado.");
            // En la cadena completa (con trazas) solo se buscan los valores distintivos.
            if (value.Contains("example.test", StringComparison.Ordinal))
                Assert.False(error!.ToString().Contains(value.Trim(), StringComparison.OrdinalIgnoreCase),
                    $"Caso {scenario}: la cadena de errores de arranque contiene un valor configurado.");
        }
    }

    // ── Parseo estricto (SR-0018.43) ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA82_ProductionHost_OrigenesConEspaciosYVacios_ProduceExactamenteLosDosOrigenes()
    {
        await using var factory = ProductionHost("https://a.com", " https://a.com, ,https://b.com");

        using var client = factory.CreateClient();

        Assert.Equal(["https://a.com", "https://b.com"], FrontendPolicy(factory.Services).Origins);
        Assert.False(FrontendPolicy(factory.Services).SupportsCredentials);
    }

    [Fact]
    public void PLAN0018_CA82_ParseAllowedOrigins_RecortaDescartaVaciosYDuplicados()
    {
        Assert.Equal(["https://a.com", "https://b.com"],
            AppUrlSettingsValidation.ParseAllowedOrigins(" https://a.com, ,https://b.com", strict: true));
        Assert.Equal(["https://a.com"],
            AppUrlSettingsValidation.ParseAllowedOrigins("https://a.com/, https://A.com", strict: true));
        Assert.Empty(AppUrlSettingsValidation.ParseAllowedOrigins("", strict: true));
    }

    [Theory]
    [InlineData("http://localhost:3100", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("https://localhost:5001", true)]
    [InlineData("http://localhost.evil.com", false)]
    [InlineData("http://localhost.evil.com:3000", false)]
    [InlineData("http://evil-localhost:3000", false)]
    [InlineData("http://user@localhost:3000", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    public void PLAN0018_CA82_IsLocalhostOrigin_ReconoceElHostParseadoNuncaElPrefijo(string origin, bool expected)
        => Assert.Equal(expected, AppUrlSettingsValidation.IsLocalhostOrigin(origin));

    // ── Development ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA82_DevelopmentHost_SinBaseUrl_LosEnlacesUsanLocalhost3000()
    {
        await using var factory = DevelopmentHost(baseUrl: "", allowedOrigins: "");

        using var client = factory.CreateClient();

        Assert.Equal(ApplicationSettings.DevelopmentDefaultBaseUrl,
            factory.Services.GetRequiredService<IApplicationSettings>().BaseUrl);
        Assert.Equal("http://localhost:3000", ApplicationSettings.DevelopmentDefaultBaseUrl);
    }

    [Fact]
    public async Task PLAN0018_CA82_DevelopmentHost_Localhost3100Aceptado_LocalhostEvilComSinAllowOriginYSinCredentials()
    {
        await using var factory = DevelopmentHost(baseUrl: "", allowedOrigins: "");
        using var client = factory.CreateClient();

        using (var allowed = await PreflightAsync(client, "http://localhost:3100"))
        {
            Assert.True(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var values),
                "http://localhost:3100 no recibió Access-Control-Allow-Origin en Development.");
            Assert.Equal(["http://localhost:3100"], values);
            Assert.False(allowed.Headers.Contains("Access-Control-Allow-Credentials"),
                "La política CORS emitió Access-Control-Allow-Credentials.");
        }

        using var rejected = await PreflightAsync(client, "http://localhost.evil.com");
        Assert.False(rejected.Headers.Contains("Access-Control-Allow-Origin"),
            "http://localhost.evil.com recibió Access-Control-Allow-Origin en Development.");
        Assert.False(rejected.Headers.Contains("Access-Control-Allow-Credentials"),
            "La política CORS emitió Access-Control-Allow-Credentials.");
    }

    [Fact]
    public async Task PLAN0018_CA82_ProductionHost_OrigenConfiguradoSinCredentials_LocalhostSinAllowOrigin()
    {
        await using var factory = ProductionHost("https://a.com", "https://a.com");
        using var client = factory.CreateClient();

        using (var allowed = await PreflightAsync(client, "https://a.com"))
        {
            Assert.True(allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var values),
                "El origen configurado no recibió Access-Control-Allow-Origin.");
            Assert.Equal(["https://a.com"], values);
            Assert.False(allowed.Headers.Contains("Access-Control-Allow-Credentials"),
                "La política CORS emitió Access-Control-Allow-Credentials.");
        }

        // Fuera de Development, localhost no se admite por defecto.
        using var localhost = await PreflightAsync(client, "http://localhost:3100");
        Assert.False(localhost.Headers.Contains("Access-Control-Allow-Origin"),
            "http://localhost:3100 recibió Access-Control-Allow-Origin fuera de Development.");
    }

    // ── Testing ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA82_ZentoryApiFactory_FijaBaseUrlYOrigenesDePrueba()
    {
        await using var factory = new ZentoryApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(TestAppUrlSettings.BaseUrl, factory.Services.GetRequiredService<IApplicationSettings>().BaseUrl);
        Assert.Equal([TestAppUrlSettings.AllowedOrigins], FrontendPolicy(factory.Services).Origins);
        Assert.False(FrontendPolicy(factory.Services).SupportsCredentials);
    }

    [Fact]
    public async Task PLAN0018_CA82_TestingHost_SinBaseUrl_NoArrancaComoFueraDeDevelopment()
    {
        // El ambiente de pruebas solo restringe (RF-23): sin App:BaseUrl falla igual que Production.
        await using var factory = new MinimalHostFactory(AppEnvironments.Testing, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]         = "true",
            [DatabaseSettings.InMemoryNameKey]        = $"zentory-app-url-testing-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]       = "false",
            [DevSeedSettings.EnabledKey]              = "false",
            [AppUrlSettingsValidation.BaseUrlSetting] = "",
        });

        var error = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(error);
        Assert.NotNull(FindInChain(error!,
            e => e.Message.Contains(AppUrlSettingsValidation.BaseUrlSetting, StringComparison.Ordinal)));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Production no admite InMemory: PostgreSQL con Host=unused, que el arranque fuera de Development nunca abre.
    private static MinimalHostFactory ProductionHost(string? baseUrl, string? allowedOrigins)
        => new(Environments.Production, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]                = "false",
            [DatabaseStartupSettings.ConnectionStringKey]    = UnusedConnectionString,
            [HangfireSettings.ServerEnabledKey]              = "false",
            [DevSeedSettings.EnabledKey]                     = "false",
            [AppUrlSettingsValidation.BaseUrlSetting]        = baseUrl,
            [AppUrlSettingsValidation.AllowedOriginsSetting] = allowedOrigins,
        });

    private static MinimalHostFactory DevelopmentHost(string? baseUrl, string? allowedOrigins)
        => new(Environments.Development, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]                = "true",
            [DatabaseSettings.InMemoryNameKey]               = $"zentory-app-url-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]              = "false",
            [DevSeedSettings.EnabledKey]                     = "false",
            [AppUrlSettingsValidation.BaseUrlSetting]        = baseUrl,
            [AppUrlSettingsValidation.AllowedOriginsSetting] = allowedOrigins,
        });

    private static CorsPolicy FrontendPolicy(IServiceProvider services)
    {
        var policy = services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(CorsExtensions.FrontendPolicy);
        Assert.NotNull(policy);
        return policy!;
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, PreflightPath);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return await client.SendAsync(request);
    }

    // Lee "Seccion:Clave" de un appsettings; null si no existe.
    private static string? ValueOf(JsonElement root, string key)
    {
        var current = root;
        foreach (var part in key.Split(':'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException? FindInChain(Exception exception, Func<InvalidOperationException, bool> predicate)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException match && predicate(match)) return match;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = FindInChain(inner, predicate);
                    if (found is not null) return found;
                }
            }
        }

        return null;
    }
}

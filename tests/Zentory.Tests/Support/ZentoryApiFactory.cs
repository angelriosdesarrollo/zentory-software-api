using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// Host de pruebas compartido (PLAN-0001, RF-12; ADR-0001 §Decisión 3) sobre el Program.cs real. Se comparte por clase
// con IClassFixture<ZentoryApiFactory>; los ajustes de un test concreto van con WithWebHostBuilder o derivando la
// factory y sobrescribiendo DefaultSettings.
//
// Toda la configuración entra con UseSetting y no con ConfigureAppConfiguration (D-1): el hosting mínimo la ve como
// línea de comandos, que gana sobre appsettings y variables de entorno y llega antes de las lecturas que hacen
// AddInfrastructure y AddJwtAuthentication al registrar servicios. En Testing, Program.cs no carga
// appsettings.Local.json, y appsettings.Development.json solo se carga en Development.
//
// La raíz de contenido es una VersionedContentRoot temporal y el almacenamiento es FakeStorageService (PLAN-0018
// RF-19 d): ningún test deja archivos bajo src/Zentory.API/App_Data.
//
// Advertencia (H-08, posición 1): las aserciones de aislamiento entre tenants no deben basarse en los filtros globales
// de EF hasta que la posición 1 los corrija; las lecturas de comprobación usan IgnoreQueryFilters().
public class ZentoryApiFactory : WebApplicationFactory<Program>
{
    // Una base InMemory por instancia (D-2): EF comparte las bases del mismo nombre dentro del proceso.
    public string DatabaseName { get; } = $"zentory-tests-{Guid.NewGuid():N}";

    // Centinela: ConfigureTestServices corre al construir el host (builder.Build()). Si el arranque falla antes,
    // como el fail-fast de UseInMemory, queda en false y prueba que no se llegó a registrar servicios.
    public bool TestServicesConfigured { get; private set; }

    private readonly VersionedContentRoot _contentRoot = new();

    public TestTokens Tokens => new(Services);

    public string ContentRootPath => _contentRoot.Path;

    protected virtual IDictionary<string, string?> DefaultSettings
    {
        get
        {
            // Ningún test toca una base real, R2 ni los servicios externos, aunque el entorno del proceso traiga sus
            // variables (MinimalHostFactory.NeutralizedMachineSettings).
            var settings = new Dictionary<string, string?>(
                MinimalHostFactory.NeutralizedMachineSettings(), StringComparer.OrdinalIgnoreCase)
            {
                [DatabaseSettings.UseInMemoryKey]   = "true",
                [DatabaseSettings.InMemoryNameKey]  = DatabaseName,
                [HangfireSettings.ServerEnabledKey] = "false",
                [DevSeedSettings.EnabledKey]        = "false",
            };
            foreach (var (key, value) in TestJwtSettings.AsSettings())
                settings[key] = value;
            // App:BaseUrl y Cors:AllowedOrigins fijos (PLAN-0018 RF-62, D-38): los enlaces de los correos salen de aquí
            // y nunca de la máquina.
            foreach (var (key, value) in TestAppUrlSettings.AsSettings())
                settings[key] = value;
            if (RaiseAuthRateLimits)
                foreach (var (key, value) in RaisedAuthRateLimitSettings())
                    settings[key] = value;
            return settings;
        }
    }

    // La suite comparte un AuthAttemptLimiter por host y varios tests repiten la misma acción con el mismo usuario:
    // con los umbrales por defecto (5 en 15 min) chocarían con el límite (architecture.md §5, D-13). Los tests de
    // límites usan su propia factory con RaiseAuthRateLimits = false para probar los valores por defecto.
    protected virtual bool RaiseAuthRateLimits => true;

    public const int RaisedAuthPermitLimit = 100_000;

    public static IEnumerable<KeyValuePair<string, string?>> RaisedAuthRateLimitSettings()
        => AuthRateLimitPolicies.Defaults
            .SelectMany(policy => policy.Rules)
            .Select(rule => new KeyValuePair<string, string?>(
                $"{rule.ConfigSection}:PermitLimit", RaisedAuthPermitLimit.ToString(CultureInfo.InvariantCulture)));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(AppEnvironments.Testing);
        builder.UseContentRoot(_contentRoot.Path);
        foreach (var (key, value) in DefaultSettings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            TestServicesConfigured = true;

            services.RemoveAll<IAiTextGenerationService>();
            services.AddSingleton<FakeAiTextGenerationService>();
            services.AddSingleton<IAiTextGenerationService>(sp => sp.GetRequiredService<FakeAiTextGenerationService>());

            services.RemoveAll<IEmailService>();
            services.AddSingleton<FakeEmailService>();
            services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<FakeEmailService>());

            // R2 neutralizado también por registro, no solo por configuración: el almacenamiento de pruebas es en
            // memoria, nunca CloudflareR2StorageService ni el disco (LocalDiskStorageService).
            services.RemoveAll<IStorageService>();
            services.AddSingleton<FakeStorageService>();
            services.AddSingleton<IStorageService>(sp => sp.GetRequiredService<FakeStorageService>());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _contentRoot.Dispose();
    }
}

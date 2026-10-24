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
// Advertencia (H-08, posición 1): las aserciones de aislamiento entre tenants no deben basarse en los filtros globales
// de EF hasta que la posición 1 los corrija; las lecturas de comprobación usan IgnoreQueryFilters().
public class ZentoryApiFactory : WebApplicationFactory<Program>
{
    // Una base InMemory por instancia (D-2): EF comparte las bases del mismo nombre dentro del proceso.
    public string DatabaseName { get; } = $"zentory-tests-{Guid.NewGuid():N}";

    // Centinela: ConfigureTestServices corre al construir el host (builder.Build()). Si el arranque falla antes,
    // como el fail-fast de UseInMemory, queda en false y prueba que no se llegó a registrar servicios.
    public bool TestServicesConfigured { get; private set; }

    public TestTokens Tokens => new(Services);

    protected virtual IDictionary<string, string?> DefaultSettings
    {
        get
        {
            var settings = new Dictionary<string, string?>
            {
                [DatabaseSettings.UseInMemoryKey]     = "true",
                [DatabaseSettings.InMemoryNameKey]    = DatabaseName,
                [HangfireSettings.ServerEnabledKey]   = "false",
                ["DevSeed:Enabled"]                   = "false",
                // Ningún test toca una base real ni R2, aunque el entorno del proceso traiga sus variables.
                ["ConnectionStrings:DefaultConnection"] = string.Empty,
                ["R2:AccountId"]                      = string.Empty,
            };
            foreach (var (key, value) in TestJwtSettings.AsSettings())
                settings[key] = value;
            return settings;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(AppEnvironments.Testing);
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

            // R2 neutralizado también por registro, no solo por configuración: el almacenamiento de pruebas es el
            // de disco local, nunca CloudflareR2StorageService.
            services.RemoveAll<IStorageService>();
            services.AddScoped<IStorageService, LocalDiskStorageService>();
        });
    }
}

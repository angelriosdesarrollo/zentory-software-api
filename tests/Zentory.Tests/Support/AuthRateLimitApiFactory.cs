using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// Host con las políticas de los endpoints de AuthController (RF-29: login, forgot-password, resend-verification,
// register, google, reset-password, verify-email) en sus valores por defecto, para los tests de esos límites (PLAN-0018
// CA-34). Las de reautenticación y organización siguen subidas: así el contador que se prueba es solo el del endpoint.
//
// La IP de la clave: TestServer no asigna Connection.RemoteIpAddress, así que todas las peticiones de un host de pruebas
// comparten la clave de IP "-" (AuthAttemptLimiter.NormalizeIp). Por eso cada test necesita su propio limitador: el
// test arma un host derivado con WithWebHostBuilder, que tiene su propio contenedor y con él su propio AuthAttemptLimiter.
public sealed class AuthRateLimitApiFactory : ZentoryApiFactory
{
    protected override bool RaiseAuthRateLimits => false;

    protected override IDictionary<string, string?> DefaultSettings
    {
        get
        {
            var settings = base.DefaultSettings;
            foreach (var (key, value) in RaisedSettingsExcept(AuthRateLimitPolicies.AuthEndpointPolicies))
                settings[key] = value;
            return settings;
        }
    }

    // Los umbrales subidos de todas las políticas salvo las nombradas, que quedan en sus valores por defecto.
    public static IEnumerable<KeyValuePair<string, string?>> RaisedSettingsExcept(IEnumerable<string> policies)
    {
        var keep = new HashSet<string>(policies, StringComparer.Ordinal);
        var sections = AuthRateLimitPolicies.Defaults
            .Where(policy => keep.Contains(policy.Name))
            .SelectMany(policy => policy.Rules)
            .Select(rule => rule.ConfigSection + ":")
            .ToList();

        return RaisedAuthRateLimitSettings()
            .Where(setting => !sections.Any(section => setting.Key.StartsWith(section, StringComparison.Ordinal)));
    }

    // Umbral por defecto de una regla (AuthRateLimitPolicies.Defaults): los tests cuentan contra él, sin literales.
    public static AuthRateLimitRule DefaultRule(string policy, AuthRateLimitPartition partition)
        => AuthRateLimitPolicies.Defaults.Single(p => p.Name == policy).Rules.Single(rule => rule.Partition == partition);

    public static int DefaultPermitLimit(string policy, AuthRateLimitPartition partition)
        => DefaultRule(policy, partition).PermitLimit;

    // Host derivado de cualquier host de pruebas cuyo limitador es el real con los umbrales por defecto de todas las
    // políticas (AuthRateLimitPolicies.Defaults), sin depender de la configuración del host. Sirve a un test de otra
    // clase que necesita un 429 real de RF-29 (p. ej. CA-130: un login rechazado con 429 no toca la sesión vigente).
    public static WebApplicationFactory<Program> WithDefaultAuthRateLimits(WebApplicationFactory<Program> host)
        => host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuthAttemptLimiter>();
            services.AddSingleton(sp => new AuthAttemptLimiter(
                sp.GetRequiredService<TimeProvider>(), AuthRateLimitPolicies.Defaults));
        }));
}

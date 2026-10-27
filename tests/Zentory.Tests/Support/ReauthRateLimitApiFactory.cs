using Zentory.Infrastructure.Services;

namespace Zentory.Tests.Support;

// Host con el límite reauth:{sub} en sus valores por defecto (5 intentos cada 15 min, RF-81 f) para los tests de ese
// límite (PLAN-0018 CA-111). Los demás límites siguen subidos: así el contador que se prueba es solo el de reautenticación
// y no el de 5 por hora de organization-delete, que la spec cubre aparte (SR-0018.29).
public sealed class ReauthRateLimitApiFactory : ZentoryApiFactory
{
    protected override bool RaiseAuthRateLimits => false;

    protected override IDictionary<string, string?> DefaultSettings
    {
        get
        {
            var settings = base.DefaultSettings;
            foreach (var (key, value) in RaisedAuthRateLimitSettings())
                if (!key.StartsWith(AuthRateLimitPolicies.AuthSection + ":Reauth:", StringComparison.Ordinal))
                    settings[key] = value;
            return settings;
        }
    }
}

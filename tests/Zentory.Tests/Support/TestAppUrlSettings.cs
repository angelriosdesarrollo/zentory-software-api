using Zentory.API.Extensions;

namespace Zentory.Tests.Support;

// App:BaseUrl y Cors:AllowedOrigins de los hosts de prueba (PLAN-0018 RF-62, D-38). Ningún appsettings versionado trae
// dominio y un host fuera de Development (Testing incluido) no arranca sin las dos claves: ZentoryApiFactory y
// MinimalHostFactory parten de estos valores (https, dominio .test reservado), y cada test los reemplaza o los vacía.
public static class TestAppUrlSettings
{
    public const string BaseUrl        = "https://app.zentory.test";
    public const string AllowedOrigins = "https://app.zentory.test";

    public static IReadOnlyDictionary<string, string> AsSettings() => new Dictionary<string, string>
    {
        [AppUrlSettingsValidation.BaseUrlSetting]        = BaseUrl,
        [AppUrlSettingsValidation.AllowedOriginsSetting] = AllowedOrigins,
    };
}

using Zentory.Infrastructure.Services;

namespace Zentory.API.Extensions;

// Configuración validada de App:BaseUrl y Cors:AllowedOrigins. AllowLocalhostOrigins solo es true en Development.
public sealed record AppUrlSettings(string BaseUrl, IReadOnlyList<string> AllowedOrigins, bool AllowLocalhostOrigins);

// Fail-fast de App:BaseUrl y Cors:AllowedOrigins (PLAN-0018 RF-62, D-38, SR-0018.17, SR-0018.43), junto a
// JwtSettingsValidation: corre al registrar CORS, antes de builder.Build(). Ningún appsettings versionado trae dominio,
// así que fuera de Development las dos claves llegan del ambiente o el host no arranca. Testing se valida igual que
// Production (el ambiente de pruebas solo restringe, RF-23): ZentoryApiFactory y MinimalHostFactory fijan valores https.
// Cada mensaje nombra la clave y la regla, nunca el valor (ni el de la entrada que falla: solo su posición).
public static class AppUrlSettingsValidation
{
    public const string BaseUrlSetting        = ApplicationSettings.BaseUrlKey;
    public const string AllowedOriginsSetting = "Cors:AllowedOrigins";

    public static AppUrlSettings Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Solo Development admite http y la ausencia de las claves.
        var isDevelopment = environment.IsDevelopment();
        var strict = !isDevelopment;

        var baseUrl = ValidateBaseUrl(configuration[BaseUrlSetting], strict, environment.EnvironmentName);
        var origins = ParseAllowedOrigins(configuration[AllowedOriginsSetting], strict);

        if (strict && origins.Count == 0)
            throw new InvalidOperationException(
                $"{AllowedOriginsSetting} is required outside {Environments.Development}: " +
                "a comma-separated list of absolute https origins " +
                $"(current environment: {environment.EnvironmentName}).");

        return new AppUrlSettings(baseUrl, origins, AllowLocalhostOrigins: isDevelopment);
    }

    // Separa por coma, recorta y descarta los vacíos; cada entrada debe ser un origen absoluto (esquema, host y puerto
    // opcional) sin ruta, query, fragmento, credenciales ni comodín. Con strict, solo https.
    public static IReadOnlyList<string> ParseAllowedOrigins(string? raw, bool strict)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var origins = new List<string>();
        var entries = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var origin = ParseOrigin(entries[i], strict, $"{AllowedOriginsSetting} entry #{i + 1}");
            if (!origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                origins.Add(origin);
        }

        return origins;
    }

    // Development: cualquier puerto de localhost, reconocido por el host parseado y nunca por prefijo
    // (http://localhost.evil.com no pasa).
    public static bool IsLocalhostOrigin(string? origin)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
           && IsBareAuthority(uri);

    private static string ValidateBaseUrl(string? raw, bool strict, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (strict)
                throw new InvalidOperationException(
                    $"{BaseUrlSetting} is required outside {Environments.Development}: " +
                    $"an absolute https URL without path (current environment: {environmentName}).");
            return ApplicationSettings.DevelopmentDefaultBaseUrl;
        }

        return ParseOrigin(raw.Trim(), strict, BaseUrlSetting);
    }

    private static string ParseOrigin(string value, bool strict, string label)
    {
        var rule = strict
            ? "must be an absolute https URL without path, query, credentials or wildcard"
            : "must be an absolute http or https URL without path, query, credentials or wildcard";

        if (value.Contains('*')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps || (!strict && uri.Scheme == Uri.UriSchemeHttp))
            || string.IsNullOrEmpty(uri.Host)
            || !IsBareAuthority(uri))
            throw new InvalidOperationException($"{label} {rule}.");

        // Forma canónica del origen (esquema://host[:puerto]), la que compara la política CORS.
        return uri.GetLeftPart(UriPartial.Authority);
    }

    // Solo esquema, host y puerto: se admite la barra final ("https://a.com/"), nada más.
    private static bool IsBareAuthority(Uri uri)
        => uri.AbsolutePath == "/"
           && string.IsNullOrEmpty(uri.Query)
           && string.IsNullOrEmpty(uri.Fragment)
           && string.IsNullOrEmpty(uri.UserInfo);
}

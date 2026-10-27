using System.Text;
using Zentory.Infrastructure.Services;

namespace Zentory.API.Extensions;

// Fail-fast de la configuración JWT (PLAN-0018 RF-31, SR-2.11, D-15): corre al registrar la autenticación, antes de
// builder.Build(), así un host mal configurado nunca atiende una petición. Cada mensaje nombra la clave que falla y la
// regla, nunca su valor.
public static class JwtSettingsValidation
{
    public const string KeySetting                      = "Jwt:Key";
    public const string CollaboratorKeySetting          = "Jwt:CollaboratorKey";
    public const string AccessTokenExpiryMinutesSetting = JwtService.AccessTokenExpiryMinutesSetting;

    // HMAC-SHA256: una clave de menos de 32 bytes es más corta que el propio hash.
    public const int MinimumKeyBytes = 32;

    public static void Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var key             = RequireKey(configuration, KeySetting);
        var collaboratorKey = RequireKey(configuration, CollaboratorKeySetting);

        // Claves distintas: un token de un esquema nunca valida en el otro (usuario frente a portal de colaboradores).
        if (string.Equals(key, collaboratorKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"{KeySetting} and {CollaboratorKeySetting} must be different.");

        // 1..15 minutos; la misma lectura que usa JwtService al emitir.
        JwtService.ReadAccessTokenLifetime(configuration);
    }

    private static string RequireKey(IConfiguration configuration, string setting)
    {
        var value = configuration[setting];
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) < MinimumKeyBytes)
            throw new InvalidOperationException(
                $"{setting} is required and must be at least {MinimumKeyBytes} bytes in UTF-8.");
        return value;
    }
}

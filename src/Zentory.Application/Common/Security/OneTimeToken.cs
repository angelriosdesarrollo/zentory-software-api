using System.Security.Cryptography;
using System.Text;

namespace Zentory.Application.Common.Security;

// Token de un solo uso de reset y verificación (PLAN-0018 RF-22 a; SR-0018.10; D-11): 32 bytes de RandomNumberGenerator
// en base64url. El valor en claro solo viaja en el enlace del correo; la base guarda su SHA-256 en hexadecimal, el mismo
// formato que RefreshToken.HashOf, y el canje busca por ese hash.
public static class OneTimeToken
{
    public const int ByteLength = 32;

    // Emite un token nuevo con su hash. El valor en claro no se guarda ni se registra en ningún lado.
    public static Issued Issue()
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(ByteLength));
        return new Issued(raw, HashOf(raw));
    }

    // SHA-256 en hexadecimal (64 caracteres) del valor en claro.
    public static string HashOf(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public readonly record struct Issued(string RawToken, string Hash);
}

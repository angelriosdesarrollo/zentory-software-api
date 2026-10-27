using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Zentory.Application.Common.Security;

public sealed record IssuedReauthNonce(string Nonce, DateTimeOffset ExpiresAt);

// Nonce de reautenticación con Google (PLAN-0018 RF-84 a-c; ADR reautenticacion-reciente §3; SR-0018.31). Emisión sin
// estado: nonce = base64url(exp ‖ HMAC-SHA256(k, sub|acción|exp)), con exp en segundos Unix (8 bytes big-endian) y k
// derivada de Jwt:Key con HKDF-SHA256 (info "zentory-reauth-nonce"): distinta de la clave de firma, sin secreto nuevo y
// rota con Jwt:Key. La validación exige exp ≥ ahora y compara el HMAC en tiempo constante. El consumo de un solo uso
// lo lleva IReauthNonceStore, no esta clase.
public sealed class ReauthNonce
{
    public const string JwtKeySetting = "Jwt:Key";
    public const string HkdfInfo      = "zentory-reauth-nonce";

    private const int ExpBytes  = sizeof(long);
    private const int MacBytes  = 32;
    private const int KeyBytes  = 32;
    private const int RawLength = ExpBytes + MacBytes;

    private readonly byte[]       _key;
    private readonly TimeProvider _time;

    public ReauthNonce(string jwtKey, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(jwtKey))
            throw new InvalidOperationException($"{JwtKeySetting} es obligatorio para emitir el nonce de reautenticación.");

        _key  = DeriveKey(jwtKey);
        _time = time;
    }

    // HKDF-SHA256 sin sal sobre los bytes UTF-8 de Jwt:Key. Público para que un test compruebe la derivación.
    public static byte[] DeriveKey(string jwtKey)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwtKey), KeyBytes,
            salt: [], info: Encoding.UTF8.GetBytes(HkdfInfo));

    public IssuedReauthNonce Issue(Guid userId, string action)
    {
        EnsureKnown(action);

        var exp = _time.GetUtcNow().Add(OrgSecurity.ReauthenticationWindow).ToUnixTimeSeconds();

        var raw = new byte[RawLength];
        BinaryPrimitives.WriteInt64BigEndian(raw.AsSpan(0, ExpBytes), exp);
        Mac(userId, action, exp).CopyTo(raw.AsSpan(ExpBytes));

        return new IssuedReauthNonce(Base64UrlEncode(raw), DateTimeOffset.FromUnixTimeSeconds(exp));
    }

    // true si el nonce lo emitió este servidor para ese usuario y esa acción y no ha vencido; expiresAt es su exp.
    // Cualquier forma inválida (null, base64url roto, longitud distinta) es simplemente false.
    public bool TryValidate(string? nonce, Guid userId, string action, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrEmpty(nonce) || !ReauthAction.IsKnown(action))
            return false;

        var raw = Base64UrlDecode(nonce);
        if (raw is null || raw.Length != RawLength)
            return false;

        // Solo la codificación canónica: los bits de relleno del último carácter permiten otra cadena con los mismos
        // bytes, y el registro de consumidos se indexa por la cadena (sin esto, una variante esquivaría el un solo uso).
        if (!string.Equals(Base64UrlEncode(raw), nonce, StringComparison.Ordinal))
            return false;

        var exp = BinaryPrimitives.ReadInt64BigEndian(raw.AsSpan(0, ExpBytes));
        if (exp < _time.GetUtcNow().ToUnixTimeSeconds())
            return false;

        // El HMAC se compara antes de interpretar exp como fecha: un exp fuera de rango no llega a FromUnixTimeSeconds.
        if (!CryptographicOperations.FixedTimeEquals(raw.AsSpan(ExpBytes), Mac(userId, action, exp)))
            return false;

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
        return true;
    }

    private byte[] Mac(Guid userId, string action, long exp)
        => HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{userId:D}|{action}|{exp}"));

    private static void EnsureKnown(string action)
    {
        if (!ReauthAction.IsKnown(action))
            throw new ArgumentOutOfRangeException(nameof(action), "Acción de reautenticación desconocida.");
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string value)
    {
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return null;

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => null!,
        };
        if (base64 is null) return null;

        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
    }
}

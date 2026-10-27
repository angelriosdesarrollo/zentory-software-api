using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Zentory.Application.Common.Security;

namespace Zentory.Infrastructure.Services;

// Registro de nonce consumidos en el IMemoryCache del proceso (PLAN-0018 RF-84 d; D-40; ADR reautenticacion-reciente
// §3, opción E′). Singleton. La clave es el SHA-256 del nonce (el nonce no se guarda en claro) y la entrada vence en el
// exp del nonce: no vive más que él. IMemoryCache no ofrece "agregar si no existe" atómico, así que comprobar y
// registrar van bajo un mismo lock. Válido con una sola instancia de la API; un reinicio vacía el registro (R-15).
public sealed class InMemoryReauthNonceStore : IReauthNonceStore
{
    public const string KeyPrefix = "reauth-nonce:";

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly object       _gate = new();

    public InMemoryReauthNonceStore(IMemoryCache cache, TimeProvider time)
    {
        _cache = cache;
        _time  = time;
    }

    public static string CacheKey(string nonce)
        => KeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();

    public bool IsConsumed(string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        lock (_gate)
            return _cache.TryGetValue(CacheKey(nonce), out _);
    }

    public bool TryConsume(string nonce, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        // Un nonce vencido no se consume: ya no sirve, y una entrada con expiración pasada no quedaría registrada.
        var remaining = expiresAt - _time.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return false;

        var key = CacheKey(nonce);
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out _))
                return false;

            // Relativa al reloj inyectado: la entrada dura lo que le queda al nonce aunque el reloj de la caché y el
            // TimeProvider difieran (en producción son el mismo).
            _cache.Set(key, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = remaining });
            return true;
        }
    }

    public void Release(string nonce)
    {
        ArgumentException.ThrowIfNullOrEmpty(nonce);

        lock (_gate)
            _cache.Remove(CacheKey(nonce));
    }
}

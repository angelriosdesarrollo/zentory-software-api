using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Zentory.Application.Common.Security;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Security;

// PLAN-0018 CA-T10B-1 (parte de CA-117; RF-84; SR-0018.31; D-40): el emisor sin estado del nonce de reautenticación
// (HMAC con clave HKDF de Jwt:Key, ligado a sub + acción + exp) y el registro de un solo uso InMemoryReauthNonceStore.
// La emisión por HTTP está en Integration/Auth/ReauthNonceTests; el uso del nonce dentro del verificador, en
// ReauthenticationVerifierTests.
public sealed class ReauthNonceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly Guid             _sub  = Guid.NewGuid();

    private ReauthNonce Nonces(string jwtKey = TestJwtSettings.Key) => new(jwtKey, _time);

    // ── Emisión y validación ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA117_Emision_VenceEnCincoMinutosYValidaParaElMismoSubYAccion()
    {
        var nonces = Nonces();

        var issued = nonces.Issue(_sub, ReauthAction.OrganizationDelete);

        Assert.Equal(Start + TimeSpan.FromMinutes(5), issued.ExpiresAt);
        Assert.Equal(OrgSecurity.ReauthenticationWindow, TimeSpan.FromMinutes(5));
        Assert.True(nonces.TryValidate(issued.Nonce, _sub, ReauthAction.OrganizationDelete, out var expiresAt));
        Assert.Equal(issued.ExpiresAt, expiresAt);
    }

    [Theory]
    [InlineData(ReauthAction.OrganizationTransfer)]
    [InlineData(ReauthAction.OrganizationTransferAccept)]
    public void PLAN0018_CA117_NonceDeOtraAccion_SeRechaza(string otherAction)
    {
        var nonces = Nonces();
        var issued = nonces.Issue(_sub, ReauthAction.OrganizationDelete);

        Assert.False(nonces.TryValidate(issued.Nonce, _sub, otherAction, out _));
    }

    [Fact]
    public void PLAN0018_CA117_NonceDeOtroSub_SeRechaza()
    {
        var nonces = Nonces();
        var issued = nonces.Issue(Guid.NewGuid(), ReauthAction.OrganizationDelete);

        Assert.False(nonces.TryValidate(issued.Nonce, _sub, ReauthAction.OrganizationDelete, out _));
    }

    [Theory]
    [InlineData(0)]   // primer byte de exp
    [InlineData(7)]   // último byte de exp
    [InlineData(8)]   // primer byte del HMAC
    [InlineData(39)]  // último byte del HMAC
    public void PLAN0018_CA117_NonceAlteradoEnUnByte_SeRechaza(int position)
    {
        var nonces = Nonces();
        var raw    = Base64UrlDecode(nonces.Issue(_sub, ReauthAction.OrganizationDelete).Nonce);

        raw[position] ^= 0x01;

        Assert.False(nonces.TryValidate(Base64UrlEncode(raw), _sub, ReauthAction.OrganizationDelete, out _));
    }

    [Fact]
    public void PLAN0018_CA117_NonceVencido_RelojEnExpiresAtMasUnSegundo_SeRechaza()
    {
        var nonces = Nonces();
        var issued = nonces.Issue(_sub, ReauthAction.OrganizationDelete);

        _time.SetUtcNow(issued.ExpiresAt);
        Assert.True(nonces.TryValidate(issued.Nonce, _sub, ReauthAction.OrganizationDelete, out _));

        _time.SetUtcNow(issued.ExpiresAt + TimeSpan.FromSeconds(1));
        Assert.False(nonces.TryValidate(issued.Nonce, _sub, ReauthAction.OrganizationDelete, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-un-nonce")]
    [InlineData("AAAA")]
    [InlineData("!!!!")]
    public void PLAN0018_CA117_NonceMalFormado_SeRechazaSinLanzar(string? nonce)
    {
        Assert.False(Nonces().TryValidate(nonce, _sub, ReauthAction.OrganizationDelete, out _));
    }

    [Fact]
    public void PLAN0018_CA117_VarianteNoCanonicaDelMismoNonce_SeRechaza()
    {
        // 40 bytes ocupan 54 caracteres base64url: los 2 bits bajos del último carácter son relleno. Otra cadena con
        // los mismos bytes esquivaría el registro de consumidos, que se indexa por la cadena.
        var nonces   = Nonces();
        var nonce    = nonces.Issue(_sub, ReauthAction.OrganizationDelete).Nonce;
        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var last     = alphabet.IndexOf(nonce[^1]);
        var variant  = nonce[..^1] + alphabet[last ^ 0x01];

        Assert.Equal(Base64UrlDecode(nonce), Base64UrlDecode(variant));
        Assert.False(nonces.TryValidate(variant, _sub, ReauthAction.OrganizationDelete, out _));
    }

    [Fact]
    public void PLAN0018_CA117_AccionDesconocida_NoSeEmite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Nonces().Issue(_sub, "organization.other"));
        Assert.False(ReauthAction.IsKnown("Organization.Delete"));
        Assert.False(ReauthAction.IsKnown(null));
    }

    // ── Clave derivada de Jwt:Key ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA117_ClaveHmac_SeDerivaDeJwtKeyConHkdf()
    {
        var issued = Nonces().Issue(_sub, ReauthAction.OrganizationDelete);
        var exp    = issued.ExpiresAt.ToUnixTimeSeconds();
        var input  = Encoding.UTF8.GetBytes($"{_sub:D}|{ReauthAction.OrganizationDelete}|{exp}");

        var derived = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(TestJwtSettings.Key), 32,
            salt: [], info: Encoding.UTF8.GetBytes("zentory-reauth-nonce"));
        var raw = Base64UrlDecode(issued.Nonce);

        Assert.Equal(40, raw.Length);
        Assert.Equal(exp, BinaryPrimitives.ReadInt64BigEndian(raw.AsSpan(0, 8)));
        Assert.Equal(HMACSHA256.HashData(derived, input), raw[8..]);
        // No es la clave de firma en crudo.
        Assert.NotEqual(HMACSHA256.HashData(Encoding.UTF8.GetBytes(TestJwtSettings.Key), input), raw[8..]);
        Assert.Equal(derived, ReauthNonce.DeriveKey(TestJwtSettings.Key));
    }

    [Fact]
    public void PLAN0018_CA117_CambiarJwtKey_InvalidaLosNonceYaEmitidos()
    {
        var issued = Nonces(TestJwtSettings.Key).Issue(_sub, ReauthAction.OrganizationDelete);

        var rotated = Nonces(TestJwtSettings.ForeignKey);

        Assert.False(rotated.TryValidate(issued.Nonce, _sub, ReauthAction.OrganizationDelete, out _));
        Assert.True(Nonces(TestJwtSettings.Key).TryValidate(issued.Nonce, _sub, ReauthAction.OrganizationDelete, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PLAN0018_CA117_SinJwtKey_ElEmisorNoSeConstruye(string jwtKey)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ReauthNonce(jwtKey, _time));
        Assert.Contains(ReauthNonce.JwtKeySetting, error.Message);
    }

    // ── InMemoryReauthNonceStore: un solo uso ───────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA117_Almacen_SegundoTryConsume_DevuelveFalse()
    {
        var (store, _) = Store();
        var issued     = Nonces().Issue(_sub, ReauthAction.OrganizationDelete);

        Assert.False(store.IsConsumed(issued.Nonce));
        Assert.True(store.TryConsume(issued.Nonce, issued.ExpiresAt));
        Assert.True(store.IsConsumed(issued.Nonce));
        Assert.False(store.TryConsume(issued.Nonce, issued.ExpiresAt));
    }

    [Fact]
    public async Task PLAN0018_CA117_Almacen_ConsumosConcurrentesDelMismoNonce_ExactamenteUnoGana()
    {
        const int contenders = 32;
        var (store, _) = Store();
        var issued     = Nonces().Issue(_sub, ReauthAction.OrganizationDelete);
        using var barrier = new Barrier(contenders);

        var results = await Task.WhenAll(Enumerable.Range(0, contenders).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return store.TryConsume(issued.Nonce, issued.ExpiresAt);
        })));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public void PLAN0018_CA117_Almacen_Release_PermiteReusar()
    {
        var (store, _) = Store();
        var issued     = Nonces().Issue(_sub, ReauthAction.OrganizationDelete);
        Assert.True(store.TryConsume(issued.Nonce, issued.ExpiresAt));

        store.Release(issued.Nonce);

        Assert.False(store.IsConsumed(issued.Nonce));
        Assert.True(store.TryConsume(issued.Nonce, issued.ExpiresAt));
    }

    [Fact]
    public void PLAN0018_CA117_Almacen_EntradaVenceEnElExpDelNonce()
    {
        var (store, clock) = Store();
        var issued         = Nonces().Issue(_sub, ReauthAction.OrganizationDelete);
        Assert.True(store.TryConsume(issued.Nonce, issued.ExpiresAt));

        clock.UtcNow = issued.ExpiresAt - TimeSpan.FromSeconds(1);
        Assert.True(store.IsConsumed(issued.Nonce));

        clock.UtcNow = issued.ExpiresAt + TimeSpan.FromSeconds(1);
        Assert.False(store.IsConsumed(issued.Nonce));
    }

    [Fact]
    public void PLAN0018_CA117_Almacen_NonceVencido_NoSeConsume()
    {
        var (store, _) = Store();
        var issued     = Nonces().Issue(_sub, ReauthAction.OrganizationDelete);

        _time.SetUtcNow(issued.ExpiresAt);

        Assert.False(store.TryConsume(issued.Nonce, issued.ExpiresAt));
        Assert.False(store.IsConsumed(issued.Nonce));
    }

    [Fact]
    public void PLAN0018_CA117_Almacen_ClaveEsElSha256DelNonce_SinElNonceEnClaro()
    {
        var nonce = Nonces().Issue(_sub, ReauthAction.OrganizationDelete).Nonce;

        var key = InMemoryReauthNonceStore.CacheKey(nonce);

        Assert.StartsWith("reauth-nonce:", key);
        Assert.Equal(
            "reauth-nonce:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant(),
            key);
        Assert.DoesNotContain(nonce, key);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private (InMemoryReauthNonceStore Store, CacheClock Clock) Store()
    {
        var clock = new CacheClock { UtcNow = Start };
        var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        return (new InMemoryReauthNonceStore(cache, _time), clock);
    }

    // Reloj de MemoryCache (ISystemClock): la expiración absoluta de las entradas se evalúa con él.
    private sealed class CacheClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Repositories;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T17, CA-T17-1 (parte de CA-36; RF-30, SR-0018.13, D-12) sobre el host real: la base guarda solo el hash del
// refresh token; refresh rota el par; un token ya rotado es reuso (401 y revocación de todos los del usuario); uno
// expirado o inexistente responde 401 REFRESH_TOKEN_INVALID; de dos refresh simultáneos con el mismo token solo uno emite
// par; POST /auth/logout responde siempre 204 y revoca el token presentado.
//
// La simultaneidad es determinista: un decorador del repositorio detiene el primer refresh justo después de leer el token
// (IsRevoked = false en su contexto) y completa ahí el segundo. El primero guarda después con el valor original de
// IsRevoked, que ya no coincide: el proveedor InMemory aplica el token de concurrencia igual que el UPDATE ... WHERE
// is_revoked = false de PostgreSQL.
public sealed class RefreshTokenTests : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword = "Owner-Refresh-Token-2026!";

    private readonly ZentoryApiFactory _factory;

    public RefreshTokenTests(ZentoryApiFactory factory) => _factory = factory;

    // ── Hash en la base ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA36_Login_LaBaseGuardaSoloElHashDelRefreshToken()
    {
        var owner = await SeedOwnerAsync();
        var session = await LoginOwnerAsync(owner);

        var rows = await TokensOfAsync(owner.UserId);
        var row = Assert.Single(rows);

        Assert.Equal(RefreshToken.HashOf(session.RefreshToken), row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        // Ninguna columna de texto de la fila contiene el token en claro.
        var textValues = typeof(RefreshToken).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(row));
        Assert.DoesNotContain(textValues, value => value is not null && value.Contains(session.RefreshToken, StringComparison.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA36_Modelo_TokenHashUnicoDe64EIsRevokedComoTokenDeConcurrencia()
    {
        using var scope = _factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>().Model.FindEntityType(typeof(RefreshToken))!;

        Assert.Null(entity.FindProperty("Token"));
        var hash = entity.FindProperty(nameof(RefreshToken.TokenHash))!;
        Assert.Equal(64, hash.GetMaxLength());
        Assert.False(hash.IsNullable);
        Assert.True(entity.FindProperty(nameof(RefreshToken.IsRevoked))!.IsConcurrencyToken);

        var index = Assert.Single(entity.GetIndexes(), i => i.Properties.Single().Name == nameof(RefreshToken.TokenHash));
        Assert.True(index.IsUnique);
        Assert.Equal("ix_refresh_tokens_token_hash", index.GetDatabaseName());
    }

    // ── Rotación ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA36_RefreshValido_RotaElParYRevocaElAnterior()
    {
        var owner = await SeedOwnerAsync();
        var first = await LoginOwnerAsync(owner);
        using var http = _factory.CreateClient();

        using var response = await RefreshAsync(http, first.RefreshToken);

        var second = await ReadSessionAsync(response);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.False(string.IsNullOrEmpty(second.RawAccessToken));

        var rows = await TokensOfAsync(owner.UserId);
        Assert.True(rows.Single(r => r.TokenHash == RefreshToken.HashOf(first.RefreshToken)).IsRevoked);
        Assert.False(rows.Single(r => r.TokenHash == RefreshToken.HashOf(second.RefreshToken)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA36_RefreshYaRotado_Responde401YRevocaTodosLosDelUsuario()
    {
        var owner = await SeedOwnerAsync();
        var other = await SeedOwnerAsync();
        var otherToken = await SeedRefreshTokenAsync(_factory.Services, other);
        var first = await LoginOwnerAsync(owner);
        using var http = _factory.CreateClient();
        using var rotated = await RefreshAsync(http, first.RefreshToken);
        var current = await ReadSessionAsync(rotated);

        using var reused = await RefreshAsync(http, first.RefreshToken);

        await AssertRefreshTokenInvalidAsync(reused);
        Assert.All(await TokensOfAsync(owner.UserId), row => Assert.True(row.IsRevoked));

        // El par vigente deja de servir; el de otro usuario no se toca.
        using var withCurrent = await RefreshAsync(http, current.RefreshToken);
        await AssertRefreshTokenInvalidAsync(withCurrent);
        using var otherRefresh = await RefreshAsync(http, otherToken);
        Assert.Equal(HttpStatusCode.OK, otherRefresh.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA36_RefreshExpirado_Responde401RefreshTokenInvalid()
    {
        var owner = await SeedOwnerAsync();
        var expired = $"test-refresh-expirado-{Guid.NewGuid():N}";
        await SeedAsync(RefreshToken.Create(owner.UserId, expired, DateTime.UtcNow.AddSeconds(-1), Guid.NewGuid()));
        using var http = _factory.CreateClient();

        using var response = await RefreshAsync(http, expired);

        await AssertRefreshTokenInvalidAsync(response);
        Assert.False(Assert.Single(await TokensOfAsync(owner.UserId)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_CA36_RefreshInexistente_Responde401RefreshTokenInvalid()
    {
        using var http = _factory.CreateClient();

        using var response = await RefreshAsync(http, $"desconocido-{Guid.NewGuid():N}");

        await AssertRefreshTokenInvalidAsync(response);
    }

    [Fact]
    public async Task PLAN0018_CA36_DosRefreshSimultaneosConElMismoToken_UnSoloParNuevoYElOtro401ComoReuso()
    {
        var owner = await SeedOwnerAsync();
        var first = await LoginOwnerAsync(owner);
        HttpResponseMessage? competitor = null;
        using var host = WithInterleaving(RefreshToken.HashOf(first.RefreshToken),
            async http => competitor = await RefreshAsync(http, first.RefreshToken));
        using var http = host.CreateClient();

        using var response = await RefreshAsync(http, first.RefreshToken);

        // El que leyó primero y guardó después pierde: 401 y reuso. El otro emitió el único par nuevo.
        await AssertRefreshTokenInvalidAsync(response);
        Assert.NotNull(competitor);
        using (competitor)
        {
            var winner = await ReadSessionAsync(competitor!);
            var rows = await TokensOfAsync(owner.UserId);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.TokenHash == RefreshToken.HashOf(winner.RefreshToken));
            Assert.All(rows, row => Assert.True(row.IsRevoked));

            using var withWinner = await RefreshAsync(http, winner.RefreshToken);
            await AssertRefreshTokenInvalidAsync(withWinner);
        }
    }

    // ── Logout ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA36_LogoutConTokenVigente_Responde204YElTokenDejaDeServir()
    {
        var owner = await SeedOwnerAsync();
        var session = await LoginOwnerAsync(owner);
        using var http = _factory.CreateClient();

        using var logout = await LogoutAsync(http, session.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True(Assert.Single(await TokensOfAsync(owner.UserId)).IsRevoked);
        using var refresh = await RefreshAsync(http, session.RefreshToken);
        await AssertRefreshTokenInvalidAsync(refresh);
    }

    [Theory]
    [InlineData("desconocido")]
    [InlineData("")]
    [InlineData(null)]
    public async Task PLAN0018_CA36_LogoutConTokenInvalido_Responde204(string? refreshToken)
    {
        using var http = _factory.CreateClient();

        using var response = await LogoutAsync(http, refreshToken is "desconocido" ? $"desconocido-{Guid.NewGuid():N}" : refreshToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA36_LogoutConTokenYaRevocadoOSinCuerpo_Responde204SinRevocarElParVigente()
    {
        var owner = await SeedOwnerAsync();
        var first = await LoginOwnerAsync(owner);
        using var http = _factory.CreateClient();
        using var rotated = await RefreshAsync(http, first.RefreshToken);
        var current = await ReadSessionAsync(rotated);

        using var logout = await LogoutAsync(http, first.RefreshToken);
        using var empty = await http.PostAsync(LogoutEndpoint, content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        using var refresh = await RefreshAsync(http, current.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA36_LogoutSimultaneoConUnRefresh_Responde204YElParNuevoTampocoSirve()
    {
        var owner = await SeedOwnerAsync();
        var first = await LoginOwnerAsync(owner);
        HttpResponseMessage? competitor = null;
        using var host = WithInterleaving(RefreshToken.HashOf(first.RefreshToken),
            async http => competitor = await RefreshAsync(http, first.RefreshToken));
        using var http = host.CreateClient();

        using var logout = await LogoutAsync(http, first.RefreshToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (competitor)
        {
            var rotated = await ReadSessionAsync(competitor!);
            Assert.All(await TokensOfAsync(owner.UserId), row => Assert.True(row.IsRevoked));
            using var refresh = await RefreshAsync(http, rotated.RefreshToken);
            await AssertRefreshTokenInvalidAsync(refresh);
        }
    }

    [Fact]
    public void PLAN0018_CA36_Logout_EsPublicaComoRefresh()
    {
        var method = typeof(Zentory.API.Controllers.V1.AuthController).GetMethod("Logout")!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
        var post = Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute), true));
        Assert.Equal("logout", ((Microsoft.AspNetCore.Mvc.HttpPostAttribute)post).Template);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<User> SeedOwnerAsync()
    {
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(OwnerPassword)).BuildAsync();
        return tenants.A.Owner;
    }

    private async Task<IssuedSession> LoginOwnerAsync(User owner)
    {
        using var http = _factory.CreateClient();
        using var response = await LoginAsync(http, owner.Email, OwnerPassword);
        return await ReadSessionAsync(response);
    }

    private async Task SeedAsync(params object[] entities)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private async Task<List<RefreshToken>> TokensOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.RefreshTokens.AsNoTracking().Where(rt => rt.UserId == userId).ToListAsync();
    }

    // Host cuyo repositorio, la primera vez que lee el token con ese hash, ejecuta la petición competidora antes de
    // devolverlo. La competidora corre completa en su propio scope; la que se detuvo sigue con lo que leyó antes.
    private WebApplicationFactory<Program> WithInterleaving(string tokenHash, Func<HttpClient, Task> competitor)
    {
        WebApplicationFactory<Program>? host = null;
        var pending = 1;
        host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRefreshTokenRepository>();
            services.AddScoped<IRefreshTokenRepository>(sp => new InterleavingRefreshTokenRepository(
                new RefreshTokenRepository(sp.GetRequiredService<ZentoryDbContext>()),
                async hash =>
                {
                    if (hash != tokenHash || Interlocked.Exchange(ref pending, 0) == 0)
                        return;
                    using var http = host!.CreateClient();
                    await competitor(http);
                }));
        }));
        return host;
    }
}

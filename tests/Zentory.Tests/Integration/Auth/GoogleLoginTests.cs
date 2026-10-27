using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 CA-28 (RF-28; SPEC-0001 CA-21.1, CA-21.2; SR-0018.15) sobre el host real con FakeGoogleTokenValidator:
// sin email_verified no se crea ni se vincula nada (403 EMAIL_NOT_VERIFIED); la cuenta nueva nace verificada y con el
// email normalizado; vincular una cuenta sin verificar anula su contraseña, la verifica y revoca todas sus sesiones; una
// verificada conserva la contraseña; y una cuenta ya vinculada a otro GoogleId responde 409 sin cambios.
public sealed class GoogleLoginTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword       = "Owner-Google-Link-2026!";
    private const string EmailNotVerifiedCode = "EMAIL_NOT_VERIFIED";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public GoogleLoginTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = WithGoogle(factory, _google);
    }

    public void Dispose() => _host.Dispose();

    // ── email_verified = false ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA28_Google_EmailNoVerificadoDeUsuarioNuevo_Devuelve403EmailNotVerifiedSinCrearCuenta()
    {
        var email   = $"nuevo-{Guid.NewGuid():N}@zentory.test";
        var subject = NewSubject();

        using var response = await PostGoogleAsync(new GoogleUserInfo(subject, email, "Nora", "Nueva", EmailVerified: false));

        await ForbiddenAssert.HasCodeAsync(response, EmailNotVerifiedCode);
        Assert.DoesNotContain(email, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await FindUserAsync(u => u.Email == email || u.GoogleId == subject));
    }

    [Fact]
    public async Task PLAN0018_CA28_Google_EmailNoVerificadoDeCuentaExistente_Devuelve403SinVincularNiTocarLaContrasena()
    {
        var owner = await SeedOwnerAsync();
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, owner);

        using var response = await PostGoogleAsync(
            new GoogleUserInfo(NewSubject(), owner.Email, owner.FirstName, owner.LastName, EmailVerified: false));

        await ForbiddenAssert.HasCodeAsync(response, EmailNotVerifiedCode);
        var stored = await GetUserAsync(owner.UserId);
        Assert.Null(stored.GoogleId);
        Assert.Equal(owner.PasswordHash, stored.PasswordHash);
        Assert.Null(stored.EmailVerifiedAt);
        Assert.False(await IsRevokedAsync(refreshToken));
    }

    // ── Usuario nuevo verificado ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA28_Google_UsuarioNuevoVerificado_SeCreaConEmailVerifiedAtYEmailNormalizado()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var local   = $"Ana-{Guid.NewGuid():N}";
        var subject = NewSubject();

        using var response = await PostGoogleAsync(
            new GoogleUserInfo(subject, $" {local}@X.com ", "Ana", "Nueva", EmailVerified: true));

        var session = await ReadSessionAsync(response);
        var created = await FindUserAsync(u => u.GoogleId == subject);
        Assert.NotNull(created);
        Assert.Equal(created.UserId.ToString(), session.AccessToken.Subject);
        Assert.Equal($"{local.ToLowerInvariant()}@x.com", created.Email);
        Assert.NotNull(created.EmailVerifiedAt);
        Assert.Null(created.PasswordHash);
    }

    [Fact]
    public async Task PLAN0018_CA28_Google_EmailDelTokenConEspaciosYMayusculas_EncuentraLaCuentaConElEmailNormalizado()
    {
        var owner = await SeedOwnerAsync(verified: true);
        var subject = NewSubject();

        using var response = await PostGoogleAsync(new GoogleUserInfo(
            subject, " " + owner.Email.ToUpperInvariant() + " ", owner.FirstName, owner.LastName, EmailVerified: true));

        var session = await ReadSessionAsync(response);
        Assert.Equal(owner.UserId.ToString(), session.AccessToken.Subject);
        Assert.Equal(subject, (await GetUserAsync(owner.UserId)).GoogleId);
    }

    // ── Vinculación de una cuenta existente ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA28_Google_CuentaConContrasenaSinVerificar_AnulaContrasenaVerificaEmailYRevocaTodosSusRefreshTokens()
    {
        var owner = await SeedOwnerAsync();
        var first  = await SeedRefreshTokenAsync(_host.Services, owner);
        var second = await SeedRefreshTokenAsync(_host.Services, owner);
        var subject = NewSubject();

        using var response = await GoogleAsync(_host.CreateClient(), _google, subject, owner);

        var session = await ReadSessionAsync(response);
        var stored = await GetUserAsync(owner.UserId);
        Assert.Equal(subject, stored.GoogleId);
        Assert.Null(stored.PasswordHash);
        Assert.NotNull(stored.EmailVerifiedAt);
        Assert.True(await IsRevokedAsync(first));
        Assert.True(await IsRevokedAsync(second));
        Assert.False(await IsRevokedAsync(session.RefreshToken));

        // Las sesiones anteriores ya no renuevan (RF-90 e: son de una sesión reemplazada) y la contraseña anterior ya no
        // entra.
        using var http = _host.CreateClient();
        using var refresh = await RefreshAsync(http, first);
        await AssertSessionReplacedAsync(refresh);
        using var login = await LoginAsync(http, owner.Email, OwnerPassword);
        await AssertGenericErrorAsync(login, CredentialsField);
    }

    [Fact]
    public async Task PLAN0018_CA28_Google_CuentaVerificadaConContrasena_ConservaLaContrasenaEIniciaSesionNueva()
    {
        var owner = await SeedOwnerAsync(verified: true);
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, owner);
        var subject = NewSubject();

        using var response = await GoogleAsync(_host.CreateClient(), _google, subject, owner);

        await ReadSessionAsync(response);
        var stored = await GetUserAsync(owner.UserId);
        Assert.Equal(subject, stored.GoogleId);
        Assert.Equal(owner.PasswordHash, stored.PasswordHash);
        // La vinculación no revoca por sí misma, pero el login con Google inicia una sesión nueva (RF-90 b).
        Assert.True(await IsRevokedAsync(refreshToken));

        using var login = await LoginAsync(_host.CreateClient(), owner.Email, OwnerPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA28_Google_CuentaVinculadaSinVerificar_AlEntrarQuedaVerificada()
    {
        // Cuenta creada con Google antes de T18: vinculada, con contraseña y sin EmailVerifiedAt.
        var subject = NewSubject();
        var owner = await SeedOwnerAsync(googleId: subject);
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, owner);
        Assert.Null(owner.EmailVerifiedAt);

        using var response = await GoogleAsync(_host.CreateClient(), _google, subject, owner);

        await ReadSessionAsync(response);
        var stored = await GetUserAsync(owner.UserId);
        Assert.NotNull(stored.EmailVerifiedAt);
        Assert.Equal(subject, stored.GoogleId);
        Assert.Equal(owner.PasswordHash, stored.PasswordHash);
        // Sesión nueva (RF-90 b): el refresh anterior queda revocado.
        Assert.True(await IsRevokedAsync(refreshToken));
    }

    [Fact]
    public async Task PLAN0018_CA28_Google_CuentaConOtroGoogleId_Devuelve409SinCambiarElGoogleIdNiRevelarDatos()
    {
        var linkedSubject = NewSubject();
        var owner = await SeedOwnerAsync(googleId: linkedSubject);
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, owner);
        var otherSubject = NewSubject();

        using var response = await GoogleAsync(_host.CreateClient(), _google, otherSubject, owner);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using (var body = JsonDocument.Parse(raw))
        {
            Assert.Equal("conflict", body.RootElement.GetProperty("error").GetString());
            Assert.Equal(GoogleLoginErrors.AccountConflictCode, body.RootElement.GetProperty("code").GetString());
        }
        Assert.DoesNotContain(owner.Email, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(linkedSubject, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(otherSubject, raw, StringComparison.Ordinal);

        var stored = await GetUserAsync(owner.UserId);
        Assert.Equal(linkedSubject, stored.GoogleId);
        Assert.Equal(owner.PasswordHash, stored.PasswordHash);
        Assert.Null(stored.EmailVerifiedAt);
        Assert.False(await IsRevokedAsync(refreshToken));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static string NewSubject() => $"google-{Guid.NewGuid():N}";

    private async Task<HttpResponseMessage> PostGoogleAsync(GoogleUserInfo info)
    {
        var idToken = _google.Register(info);
        using var http = _host.CreateClient();
        return await http.PostAsJsonAsync(GoogleEndpoint, new { idToken });
    }

    // Owner de A con contraseña; verificado o no, y opcionalmente ya vinculado a un GoogleId.
    private async Task<User> SeedOwnerAsync(bool verified = false, string? googleId = null)
    {
        var tenants = await new TenantBuilder(_factory).ForA(a =>
        {
            a.WithOwnerPassword(OwnerPassword);
            if (googleId is not null) a.WithOwnerGoogleId(googleId);
        }).BuildAsync();

        if (verified)
        {
            using var scope = _host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.UserId == tenants.A.Owner.UserId);
            user.MarkEmailVerified();
            await db.SaveChangesAsync();
        }

        return await GetUserAsync(tenants.A.Owner.UserId);
    }

    private async Task<User> GetUserAsync(Guid userId)
        => await FindUserAsync(u => u.UserId == userId) ?? throw new InvalidOperationException("Usuario no sembrado.");

    private async Task<User?> FindUserAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(predicate);
    }

    private async Task<bool> IsRevokedAsync(string rawRefreshToken)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var hash = RefreshToken.HashOf(rawRefreshToken);
        var token = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.TokenHash == hash);
        return token.IsRevoked;
    }
}

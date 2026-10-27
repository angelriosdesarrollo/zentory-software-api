using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.API.Authorization;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Common;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T20, CA-T20-1 (parte de CA-34; RF-29; SR-0018.12, SR-0018.16; D-13) sobre el host real con las políticas de
// AuthController en sus umbrales por defecto (AuthRateLimitApiFactory) y el reloj simulado. En cada caso, la petición que
// excede el cupo responde 429 RATE_LIMITED con Retry-After y el comando no corre: no hay token, correo, cuenta ni sesión
// nuevos. Los conteos salen de AuthRateLimitPolicies.Defaults, sin literales.
//
// Cada test arma su propio host (WithWebHostBuilder: contenedor y AuthAttemptLimiter propios), porque en TestServer todas
// las peticiones comparten la clave de IP "-" y un techo por IP agotado por un test bloquearía al siguiente. Con el reloj
// fijo, el Retry-After del rechazo es la ventana completa de la regla llena. T29b suma invitations/accept (RF-29 f).
public sealed class AuthRateLimitTests : IClassFixture<AuthRateLimitApiFactory>, IDisposable
{
    private const string Password      = "Owner-Limite-Auth-2026!";
    private const string WrongPassword = "Incorrecta-Limite-Auth-2026!";
    private const string NewPassword   = "Owner-Nueva-Limite-2026!";
    private const string ForwardedFor  = "X-Forwarded-For";

    private readonly AuthRateLimitApiFactory        _factory;
    private readonly FakeTimeProvider               _time;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public AuthRateLimitTests(AuthRateLimitApiFactory factory)
    {
        _factory = factory;
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_time);
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(_google);
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── login (RF-29 a; SR-0018.12) ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_Login_SextoEn15MinDeLaMismaIpYEmailConMayusculasYEspacios_429SinEmitirSesion()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.Login, AuthRateLimitPartition.IpAndEmail);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var wrong = await LoginAsync(http, owner.Email, WrongPassword))
                await AssertGenericErrorAsync(wrong, CredentialsField);
        var before = await SessionSnapshotAsync(owner.UserId);

        // " ANA@x " tras 5 con "ana@x": el email normalizado de RF-27 cae en la misma cubeta, aun con la contraseña correcta.
        using (var limited = await LoginAsync(http, $"  {owner.Email.ToUpperInvariant()}  ", Password))
            await AssertRateLimitedAsync(limited, rule.Window);
        Assert.Equal(before, await SessionSnapshotAsync(owner.UserId));

        // La cubeta es por IP + email: otro email de la misma IP sigue entrando.
        var other = await SeedOwnerAsync();
        using (var otherLogin = await LoginAsync(http, other.Email, Password))
            Assert.Equal(HttpStatusCode.OK, otherLogin.StatusCode);

        // Ventana deslizante: pasados 15 min desde los intentos, el login correcto entra y emite la sesión.
        _time.Advance(rule.Window);
        using (var allowed = await LoginAsync(http, owner.Email, Password))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.NotEqual(before, await SessionSnapshotAsync(owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA34_Login_ConXForwardedForDistintoEnCadaIntento_LaIpNoCambiaYElSextoResponde429()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.Login, AuthRateLimitPartition.IpAndEmail);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var wrong = await SendWithForwardedForAsync(http, owner.Email, WrongPassword, attempt))
                await AssertGenericErrorAsync(wrong, CredentialsField);

        // Sin ForwardedHeaders, la cabecera del cliente no da un cupo nuevo (no hay spoofing de IP).
        using var limited = await SendWithForwardedForAsync(http, owner.Email, Password, rule.PermitLimit);
        await AssertRateLimitedAsync(limited, rule.Window);
    }

    [Fact]
    public async Task PLAN0018_CA34_Login_VigesimoPrimeroDeUnaIpConEmailsDistintos_429SinEmitirSesion()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.Login, AuthRateLimitPartition.Ip);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var unknown = await LoginAsync(http, NewEmail("login-ip"), WrongPassword))
                await AssertGenericErrorAsync(unknown, CredentialsField);
        var before = await SessionSnapshotAsync(owner.UserId);

        using (var limited = await LoginAsync(http, owner.Email, Password))
            await AssertRateLimitedAsync(limited, rule.Window);
        Assert.Equal(before, await SessionSnapshotAsync(owner.UserId));
    }

    // ── forgot-password (RF-29 b; SR-0018.12) ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_ForgotPassword_CuartoEnUnaHoraDelMismoEmail_429SinTokenNiCorreo()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.ForgotPassword, AuthRateLimitPartition.Email);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var accepted = await ForgotAsync(http, owner.Email))
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await WaitForEmailAsync(Emails, ResetKind, owner.Email, expected: rule.PermitLimit);
        var tokens = (await ResetTokensOfAsync(_host.Services, owner.UserId)).Count;

        using (var limited = await ForgotAsync(http, $" {owner.Email.ToUpperInvariant()} "))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.Equal(tokens, (await ResetTokensOfAsync(_host.Services, owner.UserId)).Count);
        Assert.Equal(rule.PermitLimit, SentTo(ResetKind, owner.Email));
    }

    [Fact]
    public async Task PLAN0018_CA34_ForgotPassword_UndecimoDeUnaIpConEmailsDistintos_429SinTokenNiCorreo()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.ForgotPassword, AuthRateLimitPartition.Ip);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var accepted = await ForgotAsync(http, NewEmail("forgot-ip")))
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using (var limited = await ForgotAsync(http, owner.Email))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.Empty(await ResetTokensOfAsync(_host.Services, owner.UserId));
        Assert.Equal(0, SentTo(ResetKind, owner.Email));
    }

    // ── resend-verification (RF-29 c; SR-0018.12) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_ResendVerification_CuartoEnUnaHoraDelMismoSub_429SinTokenNiCorreo()
    {
        var (session, userId, email) = await RegisterUnverifiedAsync();
        var rule = Rule(AuthRateLimitPolicies.ResendVerification, AuthRateLimitPartition.User);
        using var bearer = Bearer(_host, session.RawAccessToken);

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var accepted = await bearer.PostAsync(ResendEndpoint, content: null))
                Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        // El correo del registro y uno por cada reenvío.
        await WaitForEmailAsync(Emails, VerificationKind, email, expected: rule.PermitLimit + 1);
        var tokens = (await VerificationTokensOfAsync(_host.Services, userId)).Count;

        using (var limited = await bearer.PostAsync(ResendEndpoint, content: null))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.Equal(tokens, (await VerificationTokensOfAsync(_host.Services, userId)).Count);
        Assert.Equal(rule.PermitLimit + 1, SentTo(VerificationKind, email));
    }

    // ── register (RF-29 d) ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_Register_CuartoEnUnaHoraDeUnaIp_429SinCuentaNueva()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var rule = Rule(AuthRateLimitPolicies.Register, AuthRateLimitPartition.Ip);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var created = await RegisterAsync(http, NewEmail("register-ip"), Password))
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var email = NewEmail("register-limitado");
        using (var limited = await RegisterAsync(http, email, Password))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.False(await UserExistsAsync(email));
        Assert.Equal(0, SentTo(VerificationKind, email));
    }

    // ── google (RF-29 e) ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_Google_UndecimoEn15MinDeUnaIp_429SinCuentaNiSesion()
    {
        var rule = Rule(AuthRateLimitPolicies.Google, AuthRateLimitPartition.Ip);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            using (var rejected = await http.PostAsJsonAsync(GoogleEndpoint, new { idToken = FakeGoogleTokenValidator.RejectedToken() }))
                Assert.NotEqual(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // Un id_token válido de una identidad nueva: sin el límite crearía la cuenta e iniciaría su sesión.
        var email = NewEmail("google-limitado");
        var idToken = _google.Register(new GoogleUserInfo($"google-{Guid.NewGuid():N}", email, "Google", "Limitado",
            EmailVerified: true));
        using (var limited = await http.PostAsJsonAsync(GoogleEndpoint, new { idToken }))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.False(await UserExistsAsync(email));
    }

    // ── reset-password y verify-email (RF-29 f; SR-0018.16) ─────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_ResetPassword_Peticion21En15MinDeUnaIp_429SinCanjearElToken()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.ResetPassword, AuthRateLimitPartition.Ip);
        var issued = OneTimeToken.Issue();
        await SeedEntitiesAsync(_host.Services, PasswordResetToken.Issue(owner.UserId, issued.Hash, Now));
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            await AssertTokenInvalidAsync(await ResetAsync(http, OneTimeToken.Issue().RawToken, NewPassword));

        using (var limited = await ResetAsync(http, issued.RawToken, NewPassword))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.Null(Assert.Single(await ResetTokensOfAsync(_host.Services, owner.UserId)).UsedAt);
        using (var oldPassword = await LoginAsync(http, owner.Email, Password))
            Assert.Equal(HttpStatusCode.OK, oldPassword.StatusCode);

        // El token era válido: pasada la ventana, el mismo canje entra.
        _time.Advance(rule.Window);
        using (var allowed = await ResetAsync(http, issued.RawToken, NewPassword))
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA34_VerifyEmail_Peticion21En15MinDeUnaIp_429SinVerificarLaCuenta()
    {
        var owner = await SeedOwnerAsync();
        var rule  = Rule(AuthRateLimitPolicies.VerifyEmail, AuthRateLimitPartition.Ip);
        var issued = OneTimeToken.Issue();
        await SeedEntitiesAsync(_host.Services,
            EmailVerificationToken.Issue(owner.UserId, owner.Email, issued.Hash, Now));
        var verifiedBefore = (await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt;
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            await AssertTokenInvalidAsync(await VerifyAsync(http, OneTimeToken.Issue().RawToken));

        using (var limited = await VerifyAsync(http, issued.RawToken))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.Null(Assert.Single(await VerificationTokensOfAsync(_host.Services, owner.UserId)).UsedAt);
        Assert.Equal(verifiedBefore, (await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt);

        _time.Advance(rule.Window);
        using (var allowed = await VerifyAsync(http, issued.RawToken))
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    // ── invitations/accept (RF-29 f; SR-0018.16) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA34_InvitationsAccept_Peticion21En15MinDeUnaIp_429SinCuentaNiMembresiaNiCanje()
    {
        var tenants = await new TenantBuilder(_factory).BuildAsync();
        var rule = Rule(AuthRateLimitPolicies.AcceptInvitation, AuthRateLimitPartition.Ip);
        var invitation = await InvitationAcceptApi.SeedInvitationAsync(_host.Services, tenants.A.OrganizationId,
            NewEmail("invitacion-limite"), OrgRole.Member, tenants.A.Owner.UserId, Now);
        using var http = _host.CreateClient();

        for (var attempt = 0; attempt < rule.PermitLimit; attempt++)
            await AssertTokenInvalidAsync(await InvitationAcceptApi.AcceptAnonymousAsync(http, OneTimeToken.Issue().RawToken));

        using (var limited = await InvitationAcceptApi.AcceptAnonymousAsync(http, invitation.RawToken))
            await AssertRateLimitedAsync(limited, rule.Window);

        Assert.False(await UserExistsAsync(invitation.Email));
        Assert.Equal(0, await InvitationAcceptApi.MembershipCountForEmailAsync(
            _host.Services, invitation.OrganizationId, invitation.Email));
        Assert.Null((await InvitationAcceptApi.InvitationRowAsync(_host.Services, invitation.Id)).AcceptedAt);

        // La invitación era válida: pasada la ventana, el mismo canje entra.
        _time.Advance(rule.Window);
        using (var allowed = await InvitationAcceptApi.AcceptAnonymousAsync(http, invitation.RawToken))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static AuthRateLimitRule Rule(string policy, AuthRateLimitPartition partition)
        => AuthRateLimitApiFactory.DefaultRule(policy, partition);

    // 429 { code: "RATE_LIMITED" } con Retry-After en segundos enteros: con el reloj fijo, la ventana completa.
    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, TimeSpan window)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AuthRateLimitAttribute.RateLimitedCode, body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("accessToken", out _));
        Assert.Equal(((int)window.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    // Login con un X-Forwarded-For distinto en cada intento (documentación de 203.0.113.0/24, RFC 5737).
    private static Task<HttpResponseMessage> SendWithForwardedForAsync(HttpClient http, string email, string password, int attempt)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LoginEndpoint) { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add(ForwardedFor, $"203.0.113.{attempt + 1}");
        return http.SendAsync(request);
    }

    private async Task<User> SeedOwnerAsync()
        => (await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync()).A.Owner;

    // Cuenta nueva sin verificar con su sesión (el registro cuenta 1 de los 3 de register, que este test no agota).
    private async Task<(IssuedSession Session, Guid UserId, string Email)> RegisterUnverifiedAsync()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var email = NewEmail("resend-limite");
        using var http = _host.CreateClient();
        using var response = await RegisterAsync(http, email, Password);
        var session = await ReadSessionAsync(response, HttpStatusCode.Created);
        await WaitForEmailAsync(Emails, VerificationKind, email);
        return (session, Guid.Parse(session.Claim("sub")!), email);
    }

    // Sesión vigente y estado de cada refresh token del usuario.
    private async Task<string> SessionSnapshotAsync(Guid userId)
    {
        var current = await CurrentSessionIdAsync(_host.Services, userId);
        var rows = (await RefreshTokensOfAsync(_host.Services, userId))
            .OrderBy(r => r.Id)
            .Select(r => $"{r.Id}:{r.SessionId}:{r.IsRevoked}");
        return $"{current}|{string.Join(",", rows)}";
    }

    private int SentTo(string kind, string email)
        => Emails.Sent.Count(sent => sent.Kind == kind && sent.ToEmail == email);

    private async Task<bool> UserExistsAsync(string email)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var normalized = EmailAddress.Normalize(email);
        return await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == normalized);
    }
}

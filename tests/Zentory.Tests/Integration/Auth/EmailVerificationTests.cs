using System.Net;
using System.Reflection;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zentory.API.Controllers.V1;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T19b sobre el host real: POST /auth/verify-email y POST /auth/resend-verification (RF-20, RF-21 c, d; RF-22;
// RF-25; RF-60; SR-0018.10, SR-0018.11 b, SR-0018.22; D-11, D-14).
//
// CA-32: el canje válido responde 204, asigna EmailVerifiedAt y GET /me devuelve emailVerified: true; un token cuyo Email
// no es el del usuario responde 422 TOKEN_INVALID; resend-verification con la cuenta ya verificada responde 202 sin correo.
// CA-33 (plantilla): el correo de verificación codifica el nombre y la URL y avisa que no se confirme si no se creó la
// cuenta.
// CA-T19B-1 (parte de CA-80): tras register, el token de verificación y su URL no aparecen en las entidades, en Hangfire
// ni en los logs.
public sealed class EmailVerificationTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Password = "Owner-Verificacion-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public EmailVerificationTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs)));
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── CA-32 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA32_VerifyEmailConTokenValido_204_AsignaEmailVerifiedAt_MeDevuelveEmailVerifiedTrue_YElMismoToken422()
    {
        var (session, userId, token) = await RegisterAndReadTokenAsync("verifica-valido");
        using var http = _host.CreateClient();
        Assert.False(await EmailVerifiedInMeAsync(session.RawAccessToken));
        var before = DateTime.UtcNow;

        using (var verified = await VerifyAsync(http, token))
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        var user = await UserRowAsync(_host.Services, userId);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.InRange(user.EmailVerifiedAt!.Value, before, DateTime.UtcNow);
        Assert.NotNull(Assert.Single(await VerificationTokensOfAsync(_host.Services, userId)).UsedAt);
        // La sesión del registro sigue vigente: verificar no la cambia.
        Assert.True(await EmailVerifiedInMeAsync(session.RawAccessToken));

        await AssertTokenInvalidAsync(await VerifyAsync(http, token));
    }

    [Fact]
    public async Task PLAN0018_CA32_VerifyEmailConTokenDeOtroEmail_Responde422TokenInvalidYLaCuentaSigueSinVerificar()
    {
        // Un token emitido para un email anterior del usuario: solo probaba control de ese buzón.
        var owner = (await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync()).A.Owner;
        var issued = OneTimeToken.Issue();
        await SeedEntitiesAsync(_host.Services,
            EmailVerificationToken.Issue(owner.UserId, NewEmail("email-anterior"), issued.Hash, DateTime.UtcNow));
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await VerifyAsync(http, issued.RawToken));

        Assert.Null((await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt);
        // El rechazo no gasta el token: queda sin usar.
        Assert.Null(Assert.Single(await VerificationTokensOfAsync(_host.Services, owner.UserId)).UsedAt);
    }

    [Fact]
    public async Task PLAN0018_CA32_VerifyEmailConTokenValidoSobreCuentaYaVerificada_204_LoGastaYNoReescribeEmailVerifiedAt()
    {
        var owner = (await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync()).A.Owner;
        var (first, second) = (OneTimeToken.Issue(), OneTimeToken.Issue());
        await SeedEntitiesAsync(_host.Services,
            EmailVerificationToken.Issue(owner.UserId, owner.Email, first.Hash, DateTime.UtcNow),
            EmailVerificationToken.Issue(owner.UserId, owner.Email, second.Hash, DateTime.UtcNow));
        using var http = _host.CreateClient();
        using (var verified = await VerifyAsync(http, first.RawToken))
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        var verifiedAt = (await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt;
        Assert.NotNull(verifiedAt);

        using (var again = await VerifyAsync(http, second.RawToken))
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);

        Assert.All(await VerificationTokensOfAsync(_host.Services, owner.UserId), t => Assert.NotNull(t.UsedAt));
        Assert.Equal(verifiedAt, (await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt);
        await AssertTokenInvalidAsync(await VerifyAsync(http, second.RawToken));
    }

    [Fact]
    public async Task PLAN0018_CA32_VerifyEmailConTokenDeA_SoloVerificaAlDuenoDelTokenYNoAB()
    {
        // El usuario sale del token, nunca de la petición.
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerPassword(Password))
            .ForB(b => b.WithOwnerPassword(Password))
            .BuildAsync();
        var issued = OneTimeToken.Issue();
        await SeedEntitiesAsync(_host.Services,
            EmailVerificationToken.Issue(tenants.A.Owner.UserId, tenants.A.Owner.Email, issued.Hash, DateTime.UtcNow));
        using var http = _host.CreateClient();

        using (var verified = await VerifyAsync(http, issued.RawToken))
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        Assert.NotNull((await UserRowAsync(_host.Services, tenants.A.Owner.UserId)).EmailVerifiedAt);
        Assert.Null((await UserRowAsync(_host.Services, tenants.B.Owner.UserId)).EmailVerifiedAt);
    }

    [Fact]
    public async Task PLAN0018_CA32_VerifyEmailConTokenInexistenteOVacio_Responde422SinVerificar()
    {
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await VerifyAsync(http, OneTimeToken.Issue().RawToken));
        await AssertValidationErrorAsync(await VerifyAsync(http, string.Empty), nameof(VerifyEmailCommand.Token));
    }

    [Fact]
    public async Task PLAN0018_CA32_ResendVerificationConCuentaYaVerificada_Responde202SinCorreoNiTokenNuevo()
    {
        var (session, userId, token) = await RegisterAndReadTokenAsync("reenvio-verificada");
        var email = (await UserRowAsync(_host.Services, userId)).Email;
        using var http = _host.CreateClient();
        using (var verified = await VerifyAsync(http, token))
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        using (var bearer = Bearer(_host, session.RawAccessToken))
        using (var resend = await bearer.PostAsync(ResendEndpoint, content: null))
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        // La cola es FIFO: si el reenvío hubiera encolado un correo, llegaría antes que este reset de control.
        using (var forgot = await ForgotAsync(http, email))
            Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        await WaitForEmailAsync(Emails, ResetKind, email);

        Assert.Single(Emails.Sent, sent => sent.Kind == VerificationKind && sent.ToEmail == email);
        Assert.Single(await VerificationTokensOfAsync(_host.Services, userId));
    }

    [Fact]
    public async Task PLAN0018_CA32_ResendVerificationSinVerificar_Responde202YEnviaUnEnlaceNuevoDesdeAppBaseUrl()
    {
        var (session, userId, first) = await RegisterAndReadTokenAsync("reenvio-sin-verificar");
        var email = (await UserRowAsync(_host.Services, userId)).Email;

        using (var bearer = Bearer(_host, session.RawAccessToken))
        using (var resend = await bearer.PostAsync(ResendEndpoint, content: null))
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        var sent = await WaitForEmailAsync(Emails, VerificationKind, email, expected: 2);
        Assert.StartsWith(TestAppUrlSettings.BaseUrl + EmailVerificationEmail.VerifyPath + "?token=", sent.Url);
        Assert.NotEqual(first, TokenOf(sent));
    }

    [Fact]
    public async Task PLAN0018_CA32_ResendVerificationSinJwt_Responde401SinEnviarCorreo()
    {
        using var http = _host.CreateClient();

        using var response = await http.PostAsync(ResendEndpoint, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(Emails.Sent, sent => sent.Kind == VerificationKind);
    }

    [Fact]
    public void PLAN0018_CA32_VerifyEmailEsAllowAnonymousYResendVerificationAuthorize_PorAccion()
    {
        // RF-21 c, d y RF-60: verify-email es pública; resend-verification exige el JWT de usuario. [AllowAnonymous] va
        // por acción: en la clase anularía el [Authorize] de resend-verification.
        Assert.Null(typeof(AuthController).GetCustomAttribute<AllowAnonymousAttribute>());

        var verify = typeof(AuthController).GetMethod(nameof(AuthController.VerifyEmail))!;
        Assert.NotNull(verify.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(verify.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("verify-email", verify.GetCustomAttribute<HttpPostAttribute>()?.Template);

        var resend = typeof(AuthController).GetMethod(nameof(AuthController.ResendVerification))!;
        Assert.NotNull(resend.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(resend.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("resend-verification", resend.GetCustomAttribute<HttpPostAttribute>()?.Template);
        // El usuario sale del JWT: la acción no recibe cuerpo.
        Assert.DoesNotContain(resend.GetParameters(), p => p.GetCustomAttribute<FromBodyAttribute>() is not null);
    }

    // ── CA-33 (plantilla) ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA33_PlantillaDeVerificacion_CodificaElNombreYLaUrl_YAvisaQueNoSeConfirmeSiNoCreasteLaCuenta()
    {
        // Plantilla real de ResendEmailService (RF-25; SR-0018.11 b, SR-0018.22), sin llamar a Resend.
        var resend  = CapturingResend.Create();
        var service = new ResendEmailService(resend, new ConfigurationBuilder().Build());
        const string hostileName = "<script>alert(1)</script> & Cía";
        var url = TestAppUrlSettings.BaseUrl + EmailVerificationEmail.VerifyPath + "?token=abc_DEF-123&x=\"><img src=x>";

        await service.SendEmailVerificationEmailAsync("owner@example.test", hostileName, url,
            new DateTime(2026, 10, 26, 15, 30, 0, DateTimeKind.Utc));

        var message = Assert.Single(CapturingResend.Of(resend).Messages);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; C&#237;a", message.HtmlBody);
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.DoesNotContain("<img", message.HtmlBody);
        Assert.Contains("href=\"" + TestAppUrlSettings.BaseUrl + "/verify-email?token=abc_DEF-123&amp;x=&quot;&gt;", message.HtmlBody);
        Assert.Contains("26/10/2026 15:30", message.HtmlBody);
        Assert.Contains("Si no creaste una cuenta en Zentory, no confirmes", message.HtmlBody);
        Assert.DoesNotContain("token", message.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PLAN0018_CA33_UrlDeVerificacion_SaleDeAppBaseUrlYCodificaElToken()
    {
        Assert.Equal(TestAppUrlSettings.BaseUrl + "/verify-email?token=a%2Bb%2F%3D%26c",
            EmailVerificationEmail.UrlFor(TestAppUrlSettings.BaseUrl, "a+b/=&c"));

        var token = OneTimeToken.Issue().RawToken;
        Assert.Equal(TestAppUrlSettings.BaseUrl + "/verify-email?token=" + token,
            EmailVerificationEmail.UrlFor(TestAppUrlSettings.BaseUrl, token));
    }

    // ── CA-T19B-1 (parte de CA-80) ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA80_Register_ElTokenDeVerificacionYSuUrlNoAparecenEnEntidadesNiEnHangfireNiEnLogs()
    {
        var email = NewEmail("verificacion-fuga");
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        using var http = _host.CreateClient();

        using (var response = await RegisterAsync(http, email, Password))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var sent  = await WaitForEmailAsync(Emails, VerificationKind, email);
        var url   = sent.Url!;
        var token = TokenOf(sent);
        Assert.Contains(token, url, StringComparison.Ordinal);

        // Control: el hash sí está guardado, así que el recorrido de entidades ve la fila del token.
        var entityStrings = await EntityStringsAsync(_host.Services);
        Assert.Contains(OneTimeToken.HashOf(token), entityStrings);
        Assert.DoesNotContain(entityStrings, value => value.Contains(token, StringComparison.Ordinal));

        var hangfireStrings = HangfireStrings(_host.Services.GetRequiredService<JobStorage>());
        Assert.DoesNotContain(hangfireStrings, value => value.Contains(token, StringComparison.Ordinal));

        Assert.NotEmpty(_logs.Entries);
        Assert.DoesNotContain(_logs.Entries, entry =>
            entry.Message.Contains(token, StringComparison.Ordinal) || entry.Message.Contains(url, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PLAN0018_CA80_ResendVerification_ElTokenNuevoYSuUrlNoAparecenEnEntidadesNiEnHangfireNiEnLogs()
    {
        var (session, userId, _) = await RegisterAndReadTokenAsync("reenvio-fuga");
        var email = (await UserRowAsync(_host.Services, userId)).Email;

        using (var bearer = Bearer(_host, session.RawAccessToken))
        using (var resend = await bearer.PostAsync(ResendEndpoint, content: null))
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        var sent  = await WaitForEmailAsync(Emails, VerificationKind, email, expected: 2);
        var token = TokenOf(sent);

        var entityStrings = await EntityStringsAsync(_host.Services);
        Assert.Contains(OneTimeToken.HashOf(token), entityStrings);
        Assert.DoesNotContain(entityStrings, value => value.Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(HangfireStrings(_host.Services.GetRequiredService<JobStorage>()),
            value => value.Contains(token, StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Entries, entry =>
            entry.Message.Contains(token, StringComparison.Ordinal) || entry.Message.Contains(sent.Url!, StringComparison.Ordinal));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Registra una cuenta nueva y devuelve su sesión, su id y el token del correo de verificación.
    private async Task<(IssuedSession Session, Guid UserId, string Token)> RegisterAndReadTokenAsync(string prefix)
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var email = NewEmail(prefix);
        using var http = _host.CreateClient();
        using var response = await RegisterAsync(http, email, Password);
        var session = await ReadSessionAsync(response, HttpStatusCode.Created);
        var token = TokenOf(await WaitForEmailAsync(Emails, VerificationKind, email));
        return (session, Guid.Parse(session.Claim("sub")!), token);
    }

    private async Task<bool> EmailVerifiedInMeAsync(string accessToken)
    {
        using var bearer = Bearer(_host, accessToken);
        using var me = await bearer.GetAsync(ProfileEndpoint);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        return profile.RootElement.GetProperty("emailVerified").GetBoolean();
    }
}

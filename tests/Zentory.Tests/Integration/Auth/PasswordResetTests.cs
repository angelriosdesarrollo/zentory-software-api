using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
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

// PLAN-0018 T19 sobre el host real: POST /auth/forgot-password y POST /auth/reset-password (RF-21 a, b; RF-22 a RF-26;
// SR-0018.10, SR-0018.11 c, SR-0018.14; D-11, D-14).
//
// CA-29: forgot-password responde igual para un email existente (con mayúsculas y espacios), inexistente o solo de
// Google, y solo la cuenta con contraseña recibe correo; con un IEmailService que tarda 2 s la respuesta no lo espera.
// CA-31: el reset válido responde 204, cambia la contraseña, revoca los refresh tokens, verifica el email y el token no
// se puede canjear dos veces; una contraseña inválida responde 422 sin consumir el token.
// CA-81: PasswordPolicy en register y reset-password (tope de 72 bytes UTF-8).
// CA-T19-1 (parte de CA-80): el token del correo no aparece en las entidades, en Hangfire ni en los logs.
//
// La cola de correos corre en el host: los tests esperan el correo por condición, en el pool de hilos, con un tope de
// seguridad que no mide rendimiento.
public sealed class PasswordResetTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OldPassword = "Owner-Antes-Reset-2026!";
    private const string NewPassword = "Owner-Despues-Reset-2026!";

    // CA-29: el doble tarda 2 s; la respuesta con email existente no puede tardar más de 500 ms sobre la de uno inexistente.
    private static readonly TimeSpan SlowSendDelay  = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AllowedOverrun = TimeSpan.FromMilliseconds(500);

    private readonly ZentoryApiFactory              _factory;
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public PasswordResetTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs)));
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── CA-29: misma respuesta y solo la cuenta con contraseña recibe correo ────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA29_ForgotPassword_EmailExistenteConMayusculas_InexistenteYSoloGoogle_Mismo200YCuerpo_SoloLaCuentaConContrasenaRecibeCorreo()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerPassword(OldPassword))
            .ForB(b => b.WithOwnerGoogleId($"google-{Guid.NewGuid():N}"))
            .BuildAsync();
        var withPassword = tenants.A.Owner;
        var googleOnly   = tenants.B.Owner;
        Assert.Null(googleOnly.PasswordHash);
        using var http = _host.CreateClient();

        // La cuenta solo de Google va primero: la cola es FIFO, así que si se hubiera encolado algo para ella llegaría
        // antes que el correo de la cuenta con contraseña.
        using var google  = await ForgotAsync(http, googleOnly.Email);
        using var unknown = await ForgotAsync(http, $"nadie-{Guid.NewGuid():N}@zentory.test");
        using var known   = await ForgotAsync(http, "  " + withPassword.Email.ToUpperInvariant() + " ");

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, google.StatusCode);
        var body = await known.Content.ReadAsStringAsync();
        Assert.Equal(body, await unknown.Content.ReadAsStringAsync());
        Assert.Equal(body, await google.Content.ReadAsStringAsync());
        using (var json = JsonDocument.Parse(body))
            Assert.Equal(ForgotPasswordResponse.Fixed.Message, json.RootElement.GetProperty("message").GetString());

        var sent = await WaitForResetEmailAsync(withPassword.Email);
        Assert.StartsWith(TestAppUrlSettings.BaseUrl + ForgotPasswordCommandHandler.ResetPath + "?token=", sent.Url);
        Assert.Single(Emails.Sent);
        Assert.DoesNotContain(Emails.Sent, email => email.ToEmail == googleOnly.Email);
        Assert.Empty(await ResetTokensOfAsync(_host.Services, googleOnly.UserId));
        Assert.Single(await ResetTokensOfAsync(_host.Services, withPassword.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA29_ForgotPassword_ConEmailServiceQueTarda2s_ExistenteNoTardaMasDe500msQueInexistente()
    {
        var owner = await SeedOwnerAsync();
        Emails.SendDelay = SlowSendDelay;
        using var http = _host.CreateClient();
        var unknownEmail = $"nadie-{Guid.NewGuid():N}@zentory.test";

        // Calentamiento de los dos caminos (JIT, primera consulta): no se mide.
        (await ForgotAsync(http, unknownEmail)).Dispose();
        (await ForgotAsync(http, owner.Email)).Dispose();

        // Se toma el mínimo de varias rondas alternadas: una pausa del recolector o de la máquina no decide el resultado,
        // y si la respuesta esperara al envío, ninguna ronda bajaría de 2 s.
        var existing    = TimeSpan.MaxValue;
        var nonexistent = TimeSpan.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var unknownElapsed = await TimeForgotAsync(http, unknownEmail);
            var knownElapsed   = await TimeForgotAsync(http, owner.Email);
            nonexistent = unknownElapsed < nonexistent ? unknownElapsed : nonexistent;
            existing    = knownElapsed < existing ? knownElapsed : existing;
        }

        Assert.True(existing - nonexistent <= AllowedOverrun,
            $"Con email existente tardó {existing.TotalMilliseconds:F0} ms y con inexistente {nonexistent.TotalMilliseconds:F0} ms.");
        Assert.True(existing < SlowSendDelay, $"La respuesta esperó el envío ({existing.TotalMilliseconds:F0} ms).");

        // El correo llega después, fuera del request.
        await WaitForResetEmailAsync(owner.Email);
    }

    [Fact]
    public async Task PLAN0018_CA29_ForgotPassword_GuardaSoloElHashDelTokenConVigenciaDe30MinYLaUrlSaleDeAppBaseUrl()
    {
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        var before = DateTime.UtcNow;

        using var response = await ForgotAsync(http, owner.Email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = TokenOf(await WaitForResetEmailAsync(owner.Email));
        // 32 bytes en base64url, sin relleno.
        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);

        var row = Assert.Single(await ResetTokensOfAsync(_host.Services, owner.UserId));
        Assert.Equal(OneTimeToken.HashOf(token), row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        Assert.Null(row.UsedAt);
        Assert.InRange(row.ExpiresAt, before.Add(PasswordResetToken.Lifetime), DateTime.UtcNow.Add(PasswordResetToken.Lifetime));
        Assert.Equal(TimeSpan.FromMinutes(30), PasswordResetToken.Lifetime);
    }

    [Fact]
    public async Task PLAN0018_CA29_PlantillaDeReset_CodificaElNombreYLaUrl()
    {
        // Plantilla real de ResendEmailService (RF-25; SR-0018.22), sin llamar a Resend.
        var resend  = CapturingResend.Create();
        var service = new ResendEmailService(resend, new ConfigurationBuilder().Build());
        const string hostileName = "<script>alert(1)</script> & Cía";
        var url = TestAppUrlSettings.BaseUrl + "/reset-password?token=abc_DEF-123&x=\"><img src=x>";

        await service.SendPasswordResetEmailAsync("owner@example.test", hostileName, url,
            new DateTime(2026, 10, 26, 15, 30, 0, DateTimeKind.Utc));

        var message = Assert.Single(CapturingResend.Of(resend).Messages);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; C&#237;a", message.HtmlBody);
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.DoesNotContain("<img", message.HtmlBody);
        Assert.Contains("href=\"" + TestAppUrlSettings.BaseUrl + "/reset-password?token=abc_DEF-123&amp;x=&quot;&gt;", message.HtmlBody);
        Assert.Contains("26/10/2026 15:30", message.HtmlBody);
        Assert.DoesNotContain("token", message.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PLAN0018_CA29_ForgotYResetPassword_LlevanAllowAnonymousPorAccionYElControladorNo()
    {
        // RF-60: [AllowAnonymous] por acción; en la clase anularía el [Authorize] de reauth-nonce.
        Assert.Null(typeof(AuthController).GetCustomAttribute<AllowAnonymousAttribute>());

        foreach (var (action, route) in new[]
                 {
                     (nameof(AuthController.ForgotPassword), "forgot-password"),
                     (nameof(AuthController.ResetPassword), "reset-password"),
                 })
        {
            var method = typeof(AuthController).GetMethod(action)!;
            Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(method.GetCustomAttribute<AuthorizeAttribute>());
            Assert.Equal(route, method.GetCustomAttribute<HttpPostAttribute>()?.Template);
        }
    }

    // ── CA-31: canje del reset ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA31_ResetValidoDeCuentaSinVerificar_204_NuevaEntraViejaNo_RefreshRevocados_EmailVerificado_YElMismoToken422()
    {
        var owner = await SeedOwnerAsync();
        Assert.Null(owner.EmailVerifiedAt);
        using var http = _host.CreateClient();
        var session = await ReadSessionAsync(await LoginAsync(http, owner.Email, OldPassword));
        var otherRefresh = await SeedRefreshTokenAsync(_host.Services, owner);
        var sessionBefore = await CurrentSessionIdAsync(_host.Services, owner.UserId);
        var token = await RequestResetTokenAsync(http, owner.Email);

        using var reset = await ResetAsync(http, token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Todos los refresh tokens del usuario quedan revocados; la sesión vigente no cambia (RF-90, interacción con RF-24).
        var refreshTokens = await RefreshTokensOfAsync(_host.Services, owner.UserId);
        Assert.Equal(2, refreshTokens.Count);
        Assert.All(refreshTokens, row => Assert.True(row.IsRevoked));
        Assert.Equal(sessionBefore, await CurrentSessionIdAsync(_host.Services, owner.UserId));
        await AssertRefreshTokenInvalidAsync(await RefreshAsync(http, session.RefreshToken));
        await AssertRefreshTokenInvalidAsync(await RefreshAsync(http, otherRefresh));

        // GET /me con el access token de la sesión vigente: el email quedó verificado (SR-0018.11 c).
        using (var bearer = Bearer(_host, session.RawAccessToken))
        using (var me = await bearer.GetAsync(ProfileEndpoint))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
            Assert.True(profile.RootElement.GetProperty("emailVerified").GetBoolean());
        }

        // El token ya canjeado responde 422 TOKEN_INVALID.
        await AssertTokenInvalidAsync(await ResetAsync(http, token, NewPassword + "-otra"));

        // La contraseña anterior ya no entra y la nueva sí.
        await AssertGenericErrorAsync(await LoginAsync(http, owner.Email, OldPassword), CredentialsField);
        using var login = await LoginAsync(http, owner.Email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA31_ResetConContrasenaDe7Caracteres_Responde422ValidationErrorSinConsumirElToken()
    {
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        var token = await RequestResetTokenAsync(http, owner.Email);

        using var invalid = await ResetAsync(http, token, "Ab1-xyz");

        await AssertValidationErrorAsync(invalid, nameof(ResetPasswordCommand.NewPassword));
        Assert.Null(Assert.Single(await ResetTokensOfAsync(_host.Services, owner.UserId)).UsedAt);
        Assert.Null((await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt);

        using var valid = await ResetAsync(http, token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA31_TokenInexistenteOVencido_Responde422TokenInvalidSinCambiarLaContrasena()
    {
        var owner = await SeedOwnerAsync();
        var expired = OneTimeToken.Issue();
        await SeedEntitiesAsync(_host.Services,
            PasswordResetToken.Create(owner.UserId, expired.Hash, DateTime.UtcNow.AddSeconds(-1)));
        using var http = _host.CreateClient();

        await AssertTokenInvalidAsync(await ResetAsync(http, OneTimeToken.Issue().RawToken, NewPassword));
        await AssertTokenInvalidAsync(await ResetAsync(http, expired.RawToken, NewPassword));

        using var login = await LoginAsync(http, owner.Email, OldPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA31_SegundoForgotPassword_InvalidaElEnlaceAnteriorYSoloSirveElNuevo()
    {
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        var first = await RequestResetTokenAsync(http, owner.Email);
        var second = await RequestResetTokenAsync(http, owner.Email, expectedEmails: 2);

        var rows = await ResetTokensOfAsync(_host.Services, owner.UserId);
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows.Single(r => r.TokenHash == OneTimeToken.HashOf(first)).UsedAt);
        Assert.Null(rows.Single(r => r.TokenHash == OneTimeToken.HashOf(second)).UsedAt);

        await AssertTokenInvalidAsync(await ResetAsync(http, first, NewPassword));
        using var reset = await ResetAsync(http, second, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA31_UsedAtEsTokenDeConcurrencia_UnCanjeConLecturaAnteriorAlOtroRecibeConflicto()
    {
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        var token = await RequestResetTokenAsync(http, owner.Email);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.True(db.Model.FindEntityType(typeof(PasswordResetToken))!
            .FindProperty(nameof(PasswordResetToken.UsedAt))!.IsConcurrencyToken);

        // Un canje leyó el token sin usar; otro lo canjea y guarda antes. El primero, al guardar, pierde (RF-22 c, D-11):
        // InMemory aplica el token de concurrencia igual que el UPDATE ... WHERE used_at IS NULL de PostgreSQL.
        var staleRead = await db.PasswordResetTokens.SingleAsync(t => t.TokenHash == OneTimeToken.HashOf(token));
        using (var winner = await ResetAsync(http, token, NewPassword))
            Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);

        staleRead.MarkUsedAt(DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    // ── CA-81: PasswordPolicy en register y reset-password ──────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA81_RegisterYReset_DosContrasenasDe100AsciiConLosMismos72BytesIniciales_Responden422ValidationError()
    {
        var prefix = new string('a', 72);
        var first  = prefix + new string('1', 28);
        var second = prefix + new string('2', 28);
        Assert.Equal(100, first.Length);
        Assert.Equal(100, second.Length);
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        var token = await RequestResetTokenAsync(http, owner.Email);

        foreach (var password in new[] { first, second })
        {
            await AssertValidationErrorAsync(await RegisterAsync(http, NewEmail("registro-reset"), password), "Password");
            await AssertValidationErrorAsync(await ResetAsync(http, token, password), nameof(ResetPasswordCommand.NewPassword));
        }

        // Ninguno consumió el token.
        Assert.Null(Assert.Single(await ResetTokensOfAsync(_host.Services, owner.UserId)).UsedAt);
    }

    [Fact]
    public async Task PLAN0018_CA81_Register_ContrasenaDe72BytesUtf8ConMultibyteSeAcepta_YDe73SeRechaza()
    {
        var (accepted, rejected) = MultibytePasswords();
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        using var http = _host.CreateClient();

        await AssertValidationErrorAsync(await RegisterAsync(http, NewEmail("registro-reset"), rejected), "Password");
        using var created = await RegisterAsync(http, NewEmail("registro-reset"), accepted);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA81_Reset_ContrasenaDe72BytesUtf8ConMultibyteSeAcepta_YDe73SeRechaza()
    {
        var (accepted, rejected) = MultibytePasswords();
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        var token = await RequestResetTokenAsync(http, owner.Email);

        await AssertValidationErrorAsync(await ResetAsync(http, token, rejected), nameof(ResetPasswordCommand.NewPassword));
        using var reset = await ResetAsync(http, token, accepted);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // BCrypt verifica la contraseña completa de 72 bytes.
        using var login = await LoginAsync(http, owner.Email, accepted);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ── CA-T19-1 (parte de CA-80): el token no se persiste ni se registra ───────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA80_ForgotPassword_ElTokenYSuUrlNoAparecenEnEntidadesNiEnHangfireNiEnLogs()
    {
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();

        using var response = await ForgotAsync(http, owner.Email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent  = await WaitForResetEmailAsync(owner.Email);
        var url   = sent.Url!;
        var token = TokenOf(sent);
        Assert.Contains(token, url, StringComparison.Ordinal);
        // Control: el hash sí está guardado, así que el recorrido de entidades ve la fila del token.
        var hash = OneTimeToken.HashOf(token);

        var entityStrings = await EntityStringsAsync(_host.Services);
        Assert.Contains(hash, entityStrings);
        Assert.DoesNotContain(entityStrings, value => value.Contains(token, StringComparison.Ordinal));

        var hangfireStrings = HangfireStrings(_host.Services.GetRequiredService<JobStorage>());
        Assert.DoesNotContain(hangfireStrings, value => value.Contains(token, StringComparison.Ordinal));

        Assert.NotEmpty(_logs.Entries);
        Assert.DoesNotContain(_logs.Entries, entry =>
            entry.Message.Contains(token, StringComparison.Ordinal) || entry.Message.Contains(url, StringComparison.Ordinal));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<User> SeedOwnerAsync()
        => (await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(OldPassword)).BuildAsync()).A.Owner;

    private static async Task<TimeSpan> TimeForgotAsync(HttpClient http, string email)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await ForgotAsync(http, email);
        stopwatch.Stop();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return stopwatch.Elapsed;
    }

    // Pide el enlace y devuelve el token del último correo de reset de ese email.
    private async Task<string> RequestResetTokenAsync(HttpClient http, string email, int expectedEmails = 1)
    {
        using var response = await ForgotAsync(http, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return TokenOf(await WaitForResetEmailAsync(email, expectedEmails));
    }

    private Task<FakeEmailService.SentEmail> WaitForResetEmailAsync(string email, int expected = 1)
        => WaitForEmailAsync(Emails, ResetKind, email, expected);

    // 30 × 'ñ' (2 bytes cada una) + 4 × '€' (3 bytes cada uno) = 72 bytes en 34 caracteres; con una 'a' más son 73. Los dos cumplen el mínimo y el máximo de caracteres: solo decide el tope de bytes.
    private static (string Accepted, string Rejected) MultibytePasswords()
    {
        var accepted = new string('ñ', 30) + new string('€', 4);
        var rejected = accepted + "a";
        Assert.Equal(PasswordPolicy.MaxUtf8Bytes, Encoding.UTF8.GetByteCount(accepted));
        Assert.Equal(PasswordPolicy.MaxUtf8Bytes + 1, Encoding.UTF8.GetByteCount(rejected));
        return (accepted, rejected);
    }
}

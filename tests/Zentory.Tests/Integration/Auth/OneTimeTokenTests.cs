using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T19b: los dos tokens de un solo uso, reset y verificación, sobre el host real (RF-22, RF-25; SR-0018.10;
// D-11).
//
// CA-30: el valor enviado decodifica a 32 bytes; la base guarda solo su SHA-256; el de reset caduca a los 30 min y el de
// verificación a las 24 h (reloj simulado); emitir uno nuevo invalida los anteriores sin usar; y dos canjes simultáneos
// del mismo token dan exactamente un éxito y un 422 TOKEN_INVALID, por el token de concurrencia UsedAt de D-11.
// CA-33 (parte de forgot-password): con Host y Origin manipulados, la URL del correo empieza por App:BaseUrl.
public sealed class OneTimeTokenTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Password      = "Owner-Token-Un-Uso-2026!";
    private const string NewPassword   = "Owner-Token-Nueva-2026!";
    private const string ForgedHost    = "evil-host.example";
    private const string ForgedOrigin  = "https://evil-origin.example";
    private const int    Repetitions   = 10;

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    public OneTimeTokenTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = factory.WithWebHostBuilder(_ => { });
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── CA-30: formato y almacenamiento ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA30_OneTimeToken_DecodificaA32Bytes_YSuHashEsElSha256HexDelValor()
    {
        var issued = Enumerable.Range(0, 50).Select(_ => OneTimeToken.Issue()).ToList();

        foreach (var token in issued)
        {
            Assert.Equal(OneTimeToken.ByteLength, DecodeBase64Url(token.RawToken).Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.RawToken))), token.Hash);
            Assert.Equal(64, token.Hash.Length);
            Assert.DoesNotContain(token.RawToken, token.Hash, StringComparison.Ordinal);
        }
        Assert.Equal(issued.Count, issued.Select(t => t.RawToken).Distinct().Count());
    }

    [Fact]
    public async Task PLAN0018_CA30_TokensDeResetYVerificacionEnviados_Decodifican32Bytes_YLaBaseGuardaSoloSuSha256()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var owner = await SeedOwnerAsync();
        var email = NewEmail("token-almacen");
        using var http = _host.CreateClient();

        using (var forgot = await ForgotAsync(http, owner.Email))
            Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        using (var register = await RegisterAsync(http, email, Password))
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var reset        = TokenOf(await WaitForEmailAsync(Emails, ResetKind, owner.Email));
        var verification = TokenOf(await WaitForEmailAsync(Emails, VerificationKind, email));

        Assert.Equal(OneTimeToken.ByteLength, DecodeBase64Url(reset).Length);
        Assert.Equal(OneTimeToken.ByteLength, DecodeBase64Url(verification).Length);

        // Control: los hashes sí están en la base, así que el recorrido ve las filas; los valores en claro no.
        var stored = await EntityStringsAsync(_host.Services);
        Assert.Contains(OneTimeToken.HashOf(reset), stored);
        Assert.Contains(OneTimeToken.HashOf(verification), stored);
        Assert.DoesNotContain(stored, value => value.Contains(reset, StringComparison.Ordinal));
        Assert.DoesNotContain(stored, value => value.Contains(verification, StringComparison.Ordinal));
    }

    // ── CA-30: caducidad (reloj simulado) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA30_TokenDeVerificacion_SirveHastaUnSegundoAntesDeLas24h_YALas24hResponde422()
    {
        Assert.Equal(TimeSpan.FromHours(24), EmailVerificationToken.Lifetime);
        var start = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(start);
        await using var host = WithClock(clock);
        await EnsureBillingPlanAsync(host.Services, Plan.Free);
        var emails = host.Services.GetRequiredService<FakeEmailService>();
        using var http = host.CreateClient();
        var (first, second) = (NewEmail("caduca-verif-a"), NewEmail("caduca-verif-b"));
        foreach (var email in new[] { first, second })
            using (var register = await RegisterAsync(http, email, Password))
                Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var firstToken  = TokenOf(await WaitForEmailAsync(emails, VerificationKind, first));
        var secondToken = TokenOf(await WaitForEmailAsync(emails, VerificationKind, second));

        clock.SetUtcNow(start + EmailVerificationToken.Lifetime - TimeSpan.FromSeconds(1));
        using (var valid = await VerifyAsync(http, firstToken))
            Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);

        clock.SetUtcNow(start + EmailVerificationToken.Lifetime);
        await AssertTokenInvalidAsync(await VerifyAsync(http, secondToken));
    }

    [Fact]
    public async Task PLAN0018_CA30_TokenDeReset_SirveHastaUnSegundoAntesDeLos30Min_YALos30MinResponde422()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), PasswordResetToken.Lifetime);
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerPassword(Password))
            .ForB(b => b.WithOwnerPassword(Password))
            .BuildAsync();
        var start = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(start);
        await using var host = WithClock(clock);
        var emails = host.Services.GetRequiredService<FakeEmailService>();
        using var http = host.CreateClient();
        foreach (var owner in new[] { tenants.A.Owner, tenants.B.Owner })
            using (var forgot = await ForgotAsync(http, owner.Email))
                Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var firstToken  = TokenOf(await WaitForEmailAsync(emails, ResetKind, tenants.A.Owner.Email));
        var secondToken = TokenOf(await WaitForEmailAsync(emails, ResetKind, tenants.B.Owner.Email));

        clock.SetUtcNow(start + PasswordResetToken.Lifetime - TimeSpan.FromSeconds(1));
        using (var valid = await ResetAsync(http, firstToken, NewPassword))
            Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);

        clock.SetUtcNow(start + PasswordResetToken.Lifetime);
        await AssertTokenInvalidAsync(await ResetAsync(http, secondToken, NewPassword));
    }

    // ── CA-30: emitir uno nuevo invalida los anteriores ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA30_ResendVerification_InvalidaElTokenDelRegistro_ElAnteriorResponde422YElNuevo204()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var email = NewEmail("reenvio-invalida");
        using var http = _host.CreateClient();
        using var register = await RegisterAsync(http, email, Password);
        var session = await ReadSessionAsync(register, HttpStatusCode.Created);
        var userId = Guid.Parse(session.Claim("sub")!);
        var first = TokenOf(await WaitForEmailAsync(Emails, VerificationKind, email));

        using (var bearer = Bearer(_host, session.RawAccessToken))
        using (var resend = await bearer.PostAsync(ResendEndpoint, content: null))
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        var second = TokenOf(await WaitForEmailAsync(Emails, VerificationKind, email, expected: 2));
        Assert.NotEqual(first, second);

        var rows = await VerificationTokensOfAsync(_host.Services, userId);
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows.Single(r => r.TokenHash == OneTimeToken.HashOf(first)).UsedAt);
        Assert.Null(rows.Single(r => r.TokenHash == OneTimeToken.HashOf(second)).UsedAt);
        Assert.All(rows, row => Assert.Equal(email, row.Email));

        await AssertTokenInvalidAsync(await VerifyAsync(http, first));
        Assert.Null((await UserRowAsync(_host.Services, userId)).EmailVerifiedAt);
        using var verified = await VerifyAsync(http, second);
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA30_SegundoForgotPassword_InvalidaElResetAnteriorSinUsar()
    {
        var owner = await SeedOwnerAsync();
        using var http = _host.CreateClient();
        for (var request = 0; request < 2; request++)
            using (var forgot = await ForgotAsync(http, owner.Email))
                Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);
        var first  = TokenOf(await WaitForEmailAsync(Emails, ResetKind, owner.Email));
        var second = TokenOf(await WaitForEmailAsync(Emails, ResetKind, owner.Email, expected: 2));

        await AssertTokenInvalidAsync(await ResetAsync(http, first, NewPassword));
        using var reset = await ResetAsync(http, second, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    // ── CA-30: canje atómico (D-11) ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA30_DosVerifyEmailSimultaneosDelMismoToken_DiezVeces_ExactamenteUn204YUn422TokenInvalid()
    {
        var owner = await SeedOwnerAsync();
        var tokens = Enumerable.Range(0, Repetitions).Select(_ => OneTimeToken.Issue()).ToList();
        await SeedEntitiesAsync(_host.Services,
            [.. tokens.Select(t => EmailVerificationToken.Issue(owner.UserId, owner.Email, t.Hash, DateTime.UtcNow))]);

        foreach (var token in tokens)
        {
            using var first  = _host.CreateClient();
            using var second = _host.CreateClient();
            var responses = await Task.WhenAll(VerifyAsync(first, token.RawToken), VerifyAsync(second, token.RawToken));

            await AssertOneSuccessAndOneTokenInvalidAsync(responses);
        }
        Assert.NotNull((await UserRowAsync(_host.Services, owner.UserId)).EmailVerifiedAt);
    }

    [Fact]
    public async Task PLAN0018_CA30_DosResetPasswordSimultaneosDelMismoToken_DiezVeces_ExactamenteUn204YUn422TokenInvalid()
    {
        var owner = await SeedOwnerAsync();
        var tokens = Enumerable.Range(0, Repetitions).Select(_ => OneTimeToken.Issue()).ToList();
        await SeedEntitiesAsync(_host.Services,
            [.. tokens.Select(t => PasswordResetToken.Issue(owner.UserId, t.Hash, DateTime.UtcNow))]);

        foreach (var token in tokens)
        {
            using var first  = _host.CreateClient();
            using var second = _host.CreateClient();
            var responses = await Task.WhenAll(
                ResetAsync(first, token.RawToken, NewPassword), ResetAsync(second, token.RawToken, NewPassword));

            await AssertOneSuccessAndOneTokenInvalidAsync(responses);
        }
    }

    [Fact]
    public async Task PLAN0018_CA30_UsedAtDeVerificacionEsTokenDeConcurrencia_UnCanjeConLecturaAnteriorAlOtroRecibeConflicto()
    {
        var owner = await SeedOwnerAsync();
        var issued = OneTimeToken.Issue();
        await SeedEntitiesAsync(_host.Services,
            EmailVerificationToken.Issue(owner.UserId, owner.Email, issued.Hash, DateTime.UtcNow));
        using var http = _host.CreateClient();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.True(db.Model.FindEntityType(typeof(EmailVerificationToken))!
            .FindProperty(nameof(EmailVerificationToken.UsedAt))!.IsConcurrencyToken);

        // Un canje leyó el token sin usar; otro lo canjea y guarda antes. El primero, al guardar, pierde (RF-22 c, D-11):
        // InMemory aplica el token de concurrencia igual que el UPDATE ... WHERE used_at IS NULL de PostgreSQL.
        var staleRead = await db.EmailVerificationTokens.SingleAsync(t => t.TokenHash == issued.Hash);
        using (var winner = await VerifyAsync(http, issued.RawToken))
            Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);

        staleRead.MarkUsedAt(DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    // ── CA-33 (forgot-password) ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA33_ForgotPassword_ConHostYOriginManipulados_LaUrlDelCorreoEmpiezaPorAppBaseUrlSinElHostManipulado()
    {
        var owner = await SeedOwnerAsync();
        // La base del cliente fija el Host de la petición en el host manipulado; Origin y X-Forwarded-Host también.
        using var http = _host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{ForgedHost}") });
        using var request = new HttpRequestMessage(HttpMethod.Post, ForgotEndpoint)
        {
            Content = JsonContent.Create(new { email = owner.Email }),
        };
        request.Headers.Host = ForgedHost;
        request.Headers.Add("Origin", ForgedOrigin);
        request.Headers.Add("X-Forwarded-Host", ForgedHost);

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await WaitForEmailAsync(Emails, ResetKind, owner.Email);
        Assert.StartsWith(TestAppUrlSettings.BaseUrl + ForgotPasswordCommandHandler.ResetPath + "?token=", sent.Url);
        Assert.DoesNotContain(ForgedHost, sent.Url!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new Uri(ForgedOrigin).Host, sent.Url!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Host con el reloj simulado; comparte la base InMemory de la factory.
    private WebApplicationFactory<Program> WithClock(FakeTimeProvider clock)
        => _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));

    private static async Task AssertOneSuccessAndOneTokenInvalidAsync(HttpResponseMessage[] responses)
    {
        var success = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        success.Dispose();
        await AssertTokenInvalidAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.NoContent));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        Assert.Matches("^[A-Za-z0-9_-]+$", value);
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    private async Task<User> SeedOwnerAsync()
        => (await new TenantBuilder(_factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync()).A.Owner;
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T19b sobre el host real: POST /auth/register deja la cuenta sin verificar y envía la verificación (RF-20,
// RF-22, RF-25, RF-26, RF-27; D-11, D-14).
//
// CA-27: email repetido con otras mayúsculas → 409 EMAIL_ALREADY_EXISTS; contraseña de 7 caracteres o de más de 128 →
// 422; registro válido → EmailVerifiedAt null, un EmailVerificationToken y el correo de verificación en FakeEmailService.
// CA-33 (parte de register): con Host y Origin manipulados, la URL del correo empieza por App:BaseUrl.
public sealed class RegisterTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Password     = "Registro-Verificacion-2026!";
    private const string ForgedHost   = "evil-host.example";
    private const string ForgedOrigin = "https://evil-origin.example";

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    public RegisterTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = factory.WithWebHostBuilder(_ => { });
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── CA-27 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA27_Register_EmailExistenteConOtrasMayusculas_Responde409EmailAlreadyExistsSinTokenNiCorreo()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var existing = (await new TenantBuilder(_factory).BuildAsync()).A.Owner;
        var tokensBefore = await VerificationTokensOfAsync(_host.Services, existing.UserId);
        using var http = _host.CreateClient();

        using var response = await RegisterAsync(http, " " + existing.Email.ToUpperInvariant() + " ", Password);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            Assert.Equal("EMAIL_ALREADY_EXISTS", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(tokensBefore.Count, (await VerificationTokensOfAsync(_host.Services, existing.UserId)).Count);
        Assert.Equal(1, await UsersWithEmailAsync(existing.Email));
        Assert.DoesNotContain(Emails.Sent, sent => sent.ToEmail == existing.Email);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(129)]
    public async Task PLAN0018_CA27_Register_ContrasenaDe7ODeMasDe128Caracteres_Responde422SinCrearLaCuenta(int length)
    {
        // 129 caracteres ASCII superan también el tope de 72 bytes de PasswordPolicy (RF-26): el 422 se cumple por la
        // política vigente (decisión registrada: 128 caracteres frente a 72 bytes).
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var password = new string('a', length - 2) + "1!";
        Assert.Equal(length, password.Length);
        var email = NewEmail("registro-politica");
        using var http = _host.CreateClient();

        await AssertValidationErrorAsync(await RegisterAsync(http, email, password), nameof(RegisterCommand.Password));

        Assert.Equal(0, await UsersWithEmailAsync(email));
        Assert.DoesNotContain(Emails.Sent, sent => sent.ToEmail == email);
    }

    [Fact]
    public async Task PLAN0018_CA27_RegistroValido_CuentaSinVerificar_UnTokenDeVerificacionDeSuEmail_YElCorreoEnFakeEmailService()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var localPart = $"Registro-Valido-{Guid.NewGuid():N}";
        var expectedEmail = $"{localPart.ToLowerInvariant()}@zentory.test";
        using var http = _host.CreateClient();

        using var response = await RegisterAsync(http, $"  {localPart}@Zentory.TEST ", Password);

        // El registro sigue iniciando la sesión (RF-90 b): el sid del token es el vigente.
        var session = await ReadSessionAsync(response, HttpStatusCode.Created);
        var userId = Guid.Parse(session.Claim("sub")!);
        Assert.Equal(SessionIdOf(session.RawAccessToken), await CurrentSessionIdAsync(_host.Services, userId));

        var user = await UserRowAsync(_host.Services, userId);
        Assert.Equal(expectedEmail, user.Email);
        Assert.Null(user.EmailVerifiedAt);

        var token = Assert.Single(await VerificationTokensOfAsync(_host.Services, userId));
        Assert.Equal(expectedEmail, token.Email);
        Assert.Null(token.UsedAt);

        var sent = await WaitForEmailAsync(Emails, VerificationKind, expectedEmail);
        Assert.StartsWith(TestAppUrlSettings.BaseUrl + EmailVerificationEmail.VerifyPath + "?token=", sent.Url);
        Assert.Equal(OneTimeToken.HashOf(TokenOf(sent)), token.TokenHash);
        Assert.Single(Emails.Sent, email => email.ToEmail == expectedEmail);
    }

    // ── CA-33 (register) ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA33_Register_ConHostYOriginManipulados_LaUrlDelCorreoEmpiezaPorAppBaseUrlSinElHostManipulado()
    {
        await EnsureBillingPlanAsync(_host.Services, Plan.Free);
        var email = NewEmail("registro-host");
        // La base del cliente fija el Host de la petición en el host manipulado; Origin y X-Forwarded-Host también.
        using var http = _host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{ForgedHost}") });
        using var request = new HttpRequestMessage(HttpMethod.Post, RegisterEndpoint)
        {
            Content = JsonContent.Create(new
            {
                email,
                password      = Password,
                firstName     = "Registro",
                lastName      = "Host",
                orgName       = $"Org host {Guid.NewGuid():N}",
                legalType     = LegalType.Freelance,
                acceptedTerms = true,
            }),
        };
        request.Headers.Host = ForgedHost;
        request.Headers.Add("Origin", ForgedOrigin);
        request.Headers.Add("X-Forwarded-Host", ForgedHost);

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = await WaitForEmailAsync(Emails, VerificationKind, email);
        Assert.StartsWith(TestAppUrlSettings.BaseUrl + EmailVerificationEmail.VerifyPath + "?token=", sent.Url);
        Assert.DoesNotContain(ForgedHost, sent.Url!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new Uri(ForgedOrigin).Host, sent.Url!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> UsersWithEmailAsync(string email)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == email);
    }
}

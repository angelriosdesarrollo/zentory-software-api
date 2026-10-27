using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Zentory.Application.CollaboratorPortal;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 CA-37 (SR-2.12) y CA-45 (RF-31, G-9) sobre el host real: el access token se rechaza con 401 un segundo
// después de expirar (ClockSkew = 0, en los dos esquemas), con una firma ajena, con la organización activa de otra
// organización sin membresía o en el esquema equivocado; y su expiración sale de Jwt:AccessTokenExpiryMinutes, sin
// 15 * 60 fijo en los handlers. Además, la parte de M1 de CA-27 (RF-27: email normalizado en login, Google y register)
// y de CA-32 (RF-20: emailVerified en GET /me y en la sesión).
public sealed partial class TokenValidationTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ProtectedEndpoint    = "/api/v1/organization/profile";
    private const string CollaboratorEndpoint = "/api/v1/collaborator-portal/me";
    private const string RegisterEndpoint     = "/api/v1/auth/register";

    // ── CA-37 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA37_TokenDeUsuario_ExpiradoHaceUnSegundo_Devuelve401()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var now = DateTime.UtcNow;
        var sessionId = await AuthSessionApi.StartSessionAsync(factory.Services, tenants.A.Owner);
        var token = SignOutsideHost(tenants.A, sessionId, TestJwtSettings.Key, notBefore: now.AddMinutes(-15), expires: now.AddSeconds(-1));

        using var response = await GetProtectedAsync(token);

        // Con el ClockSkew por defecto de JwtBearer (5 min) este token aún pasaría.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA37_TokenDeUsuario_MismosClaimsVigente_Devuelve200()
    {
        // Control del anterior: con la misma firma y claims, solo la expiración decide el 401.
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var now = DateTime.UtcNow;
        var sessionId = await AuthSessionApi.StartSessionAsync(factory.Services, tenants.A.Owner);
        var token = SignOutsideHost(tenants.A, sessionId, TestJwtSettings.Key, notBefore: now.AddMinutes(-1), expires: now.AddMinutes(1));

        using var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA37_TokenDeUsuario_FirmadoConClaveAjena_Devuelve401()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var now = DateTime.UtcNow;
        var sessionId = await AuthSessionApi.StartSessionAsync(factory.Services, tenants.A.Owner);
        var token = SignOutsideHost(tenants.A, sessionId, TestJwtSettings.ForeignKey, notBefore: now.AddMinutes(-1), expires: now.AddMinutes(10));

        using var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA37_TokenDeUsuario_ConOrganizacionActivaDeOtraOrganizacionSinMembresia_Devuelve401()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        // Firma válida del host (JwtService), pero el owner de A no tiene membresía en B.
        var token = factory.Tokens.ForUser(tenants.A.Owner, tenants.B.Organization, OrgRole.Owner, tenants.B.Plan);

        using var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA37_TokenDeColaborador_EnEndpointDeUsuario_Devuelve401()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var collaboratorId = Guid.NewGuid();
        var token = factory.Tokens.ForCollaborator(
            $"collaborator-{Guid.NewGuid():N}@zentory.test", [collaboratorId], collaboratorId,
            tenants.A.OrganizationId, tenants.A.Organization.Name, CollaboratorAuthMethod.MagicLink);

        using var response = await GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA37_TokenDeUsuario_EnEndpointDeColaborador_Devuelve401()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var token = factory.Tokens.ForUser(tenants.A.Owner, tenants.A.Organization, OrgRole.Owner, tenants.A.Plan);

        using var response = await GetAsync(CollaboratorEndpoint, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA37_TokenDeColaborador_ExpiradoHaceUnSegundo_Devuelve401YVigenteNo()
    {
        // ClockSkew = 0 también en CollaboratorScheme: mismos claims y firma, solo cambia la ventana de validez.
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var collaboratorId = Guid.NewGuid();
        var issued = factory.Tokens.ForCollaborator(
            $"collaborator-{Guid.NewGuid():N}@zentory.test", [collaboratorId], collaboratorId,
            tenants.A.OrganizationId, tenants.A.Organization.Name, CollaboratorAuthMethod.MagicLink);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(issued).Claims
            .Where(c => c.Type is not ("exp" or "nbf" or "iat" or "aud" or "iss"))
            .ToList();
        var now = DateTime.UtcNow;
        var expired = Sign(claims, TestJwtSettings.CollaboratorKey, notBefore: now.AddMinutes(-15), expires: now.AddSeconds(-1));
        var fresh   = Sign(claims, TestJwtSettings.CollaboratorKey, notBefore: now.AddMinutes(-1),  expires: now.AddMinutes(1));

        using var expiredResponse = await GetAsync(CollaboratorEndpoint, expired);
        using var freshResponse   = await GetAsync(CollaboratorEndpoint, fresh);

        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, freshResponse.StatusCode);
    }

    // ── CA-27 (RF-27): email normalizado al buscar y al guardar ───────────────────────────────────────────────

    [Theory]
    [InlineData("upper")]
    [InlineData("spaces")]
    [InlineData("both")]
    public async Task PLAN0018_CA27_Login_ConEmailEnMayusculasOEspacios_Autentica(string variant)
    {
        var password = $"Clave-{Guid.NewGuid():N}";
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(password)).BuildAsync();
        var email = tenants.A.Owner.Email;
        var sent = variant switch
        {
            "upper"  => email.ToUpperInvariant(),
            "spaces" => "  " + email + "  ",
            _        => "  " + email.ToUpperInvariant() + " ",
        };

        using var http = factory.CreateClient();
        using var response = await AuthSessionApi.LoginAsync(http, sent, password);

        var session = await AuthSessionApi.ReadSessionAsync(response);
        Assert.Equal(tenants.A.Owner.UserId.ToString(), session.AccessToken.Subject);
    }

    [Fact]
    public async Task PLAN0018_CA27_Login_ConEmailEnMayusculasYClaveMala_Devuelve422GenericoIgualQueEmailInexistente()
    {
        var password = $"Clave-{Guid.NewGuid():N}";
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(password)).BuildAsync();

        using var http = factory.CreateClient();
        using var wrong   = await AuthSessionApi.LoginAsync(http, tenants.A.Owner.Email.ToUpperInvariant(), password + "x");
        using var unknown = await AuthSessionApi.LoginAsync(http, $"NADIE-{Guid.NewGuid():N}@Zentory.test", password);

        await AuthSessionApi.AssertGenericErrorAsync(wrong, AuthSessionApi.CredentialsField);
        await AuthSessionApi.AssertGenericErrorAsync(unknown, AuthSessionApi.CredentialsField);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PLAN0018_CA27_Google_ConEmailEnMayusculasYEspacios_EnlazaAlUsuarioExistente()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var owner = tenants.A.Owner;
        var google = new FakeGoogleTokenValidator();
        await using var host = AuthSessionApi.WithGoogle(factory, google);
        using var http = host.CreateClient();

        var idToken = google.Register(new GoogleUserInfo(
            $"google-{Guid.NewGuid():N}", " " + owner.Email.ToUpperInvariant() + " ", owner.FirstName, owner.LastName,
            EmailVerified: true));
        using var response = await http.PostAsJsonAsync(AuthSessionApi.GoogleEndpoint, new { idToken });

        var session = await AuthSessionApi.ReadSessionAsync(response);
        Assert.Equal(owner.UserId.ToString(), session.AccessToken.Subject);
    }

    [Fact]
    public async Task PLAN0018_CA27_Register_ConEmailEnMayusculasYEspacios_GuardaElEmailNormalizadoYElRepetidoDa409()
    {
        // InMemory no aplica ck_users_email_normalized: este test es el que detecta si se pierde la normalización.
        await AuthSessionApi.EnsureBillingPlanAsync(factory.Services, Plan.Free);
        var localPart = $"Registro-{Guid.NewGuid():N}";
        var sent = $"  {localPart.ToUpperInvariant()}@Zentory.TEST ";
        var expected = $"{localPart.ToLowerInvariant()}@zentory.test";

        using var http = factory.CreateClient();
        using var created = await RegisterAsync(http, sent);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
            Assert.Equal(expected, body.RootElement.GetProperty("user").GetProperty("email").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var stored = await db.Users.IgnoreQueryFilters()
                .Where(u => u.Email.ToLower().Contains(localPart.ToLowerInvariant()))
                .Select(u => u.Email)
                .ToListAsync();
            Assert.Equal([expected], stored);
        }

        // El mismo email con otras mayúsculas ya existe.
        using var repeated = await RegisterAsync(http, expected.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using var conflict = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync());
        Assert.Equal("EMAIL_ALREADY_EXISTS", conflict.RootElement.GetProperty("code").GetString());
    }

    // ── CA-32 (RF-20): emailVerified en GET /me y en la sesión de login ───────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PLAN0018_CA32_GetMeYLogin_DevuelvenEmailVerifiedSegunEmailVerifiedAt(bool verified)
    {
        var password = $"Clave-{Guid.NewGuid():N}";
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(password)).BuildAsync();
        if (verified)
            await MarkVerifiedAsync(tenants.A.Owner.UserId);
        using var http = factory.CreateClient();

        using var login = await AuthSessionApi.LoginAsync(http, tenants.A.Owner.Email, password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var session = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal(verified, session.RootElement.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        using var me = await GetAsync(AuthSessionApi.ProfileEndpoint, session.RootElement.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(verified, profile.RootElement.GetProperty("emailVerified").GetBoolean());
    }

    // ── CA-45 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA45_ExpiracionConfiguradaEnDiezMinutos_TokenEmitidoCaducaALosDiezMinutos()
    {
        const int minutes = 10;
        await using var host = WithExpiryMinutes(minutes);
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var sessionId = await AuthSessionApi.StartSessionAsync(factory.Services, tenants.A.Owner);

        using var scope = host.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
        Assert.Equal(TimeSpan.FromMinutes(minutes), jwt.AccessTokenLifetime);

        var before = TruncateToSeconds(DateTime.UtcNow);
        var issued = jwt.GenerateAccessToken(tenants.A.Owner, tenants.A.Organization, OrgRole.Owner, tenants.A.Plan, sessionId);
        var after = DateTime.UtcNow;

        // exp viaja en segundos Unix: se compara contra la ventana de emisión truncada a segundos.
        var validTo = new JwtSecurityTokenHandler().ReadJwtToken(issued).ValidTo;
        Assert.InRange(validTo, before.AddMinutes(minutes), after.AddMinutes(minutes));

        // Y el host que la emitió la acepta mientras está vigente.
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued);
        using var response = await http.GetAsync(ProtectedEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA45_LoginConExpiracionDeDiezMinutos_DevuelveExpiresIn600YTokenQueCaducaALosDiezMinutos()
    {
        const int minutes = 10;
        var password = $"Clave-{Guid.NewGuid():N}";
        await using var host = WithExpiryMinutes(minutes);
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(password)).BuildAsync();
        using var http = host.CreateClient();

        var before = TruncateToSeconds(DateTime.UtcNow);
        using var response = await AuthSessionApi.LoginAsync(http, tenants.A.Owner.Email, password);
        var after = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(minutes * 60, body.RootElement.GetProperty("expiresIn").GetInt32());
        var validTo = new JwtSecurityTokenHandler()
            .ReadJwtToken(body.RootElement.GetProperty("accessToken").GetString()).ValidTo;
        Assert.InRange(validTo, before.AddMinutes(minutes), after.AddMinutes(minutes));
    }

    [Fact]
    public void PLAN0018_CA45_Src_NingunHandlerNiServicioFijaQuinceMinutosEnSegundos()
    {
        var offending = SourceFiles("src")
            .SelectMany(file => File.ReadAllLines(Path.Combine(RepoRoot.Path, file))
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(item => FifteenMinutesInSeconds().IsMatch(item.line))
            .Select(item => $"{item.file}:{item.number}")
            .ToList();

        Assert.True(offending.Count == 0, "Expiración fija de 15 min en segundos: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA45_DetectorDeQuinceMinutos_EncuentraLasFormasConocidas()
    {
        // Control del anterior: las formas que tenían AuthSessionIssuer y SwitchActiveOrganizationCommand.
        Assert.Matches(FifteenMinutesInSeconds(), "private const int AccessTokenExpiresInSeconds = 15 * 60; // 15 minutes");
        Assert.Matches(FifteenMinutesInSeconds(), "var expiresIn = 15*60;");
        Assert.DoesNotMatch(FifteenMinutesInSeconds(), "(int)_jwt.AccessTokenLifetime.TotalSeconds,");
        Assert.DoesNotMatch(FifteenMinutesInSeconds(), "TimeSpan.FromMinutes(15)");
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"\b15\s*\*\s*60\b")]
    private static partial Regex FifteenMinutesInSeconds();

    private WebApplicationFactory<Program> WithExpiryMinutes(int minutes)
        => factory.WithWebHostBuilder(builder =>
            builder.UseSetting(TestJwtSettings.ExpirySetting, minutes.ToString(CultureInfo.InvariantCulture)));

    private static DateTime TruncateToSeconds(DateTime value)
        => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    private Task<HttpResponseMessage> GetProtectedAsync(string token) => GetAsync(ProtectedEndpoint, token);

    private async Task<HttpResponseMessage> GetAsync(string endpoint, string token)
    {
        using var http = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private Task<HttpResponseMessage> RegisterAsync(HttpClient http, string email)
        => http.PostAsJsonAsync(RegisterEndpoint, new
        {
            email,
            password      = $"Clave-{Guid.NewGuid():N}",
            firstName     = "Registro",
            lastName      = "Prueba",
            orgName       = $"Org registro {Guid.NewGuid():N}",
            legalType     = LegalType.Freelance,
            acceptedTerms = true,
        });

    private async Task MarkVerifiedAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.UserId == userId);
        user.MarkEmailVerified();
        await db.SaveChangesAsync();
    }

    // Réplica fuera del host de lo que firma JwtService para el owner del tenant (mismos claims, issuer, audience y
    // algoritmo): cada test varía solo la clave o la ventana de validez.
    private static string SignOutsideHost(
        SeededTenant tenant, Guid sessionId, string signingKey, DateTime notBefore, DateTime expires)
    {
        User user = tenant.Owner;
        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub,   user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Sid,   sessionId.ToString("D")),
            new Claim("role",            user.Role),
            new Claim("initials",        "TV"),
            new Claim("active_org_id",   tenant.OrganizationId.ToString()),
            new Claim("active_org_role", OrgRole.Owner),
            new Claim("plan",            tenant.Plan),
            new Claim("legal_type",      tenant.Organization.LegalType),
        ];
        return Sign(claims, signingKey, notBefore, expires);
    }

    private static string Sign(IEnumerable<Claim> claims, string signingKey, DateTime notBefore, DateTime expires)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:             TestJwtSettings.Issuer,
            audience:           TestJwtSettings.Audience,
            claims:             claims,
            notBefore:          notBefore,
            expires:            expires,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Archivos .cs bajo la carpeta, relativos al repo y sin bin/ ni obj/.
    private static IEnumerable<string> SourceFiles(string folder)
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, folder), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"));
}

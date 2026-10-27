using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Auth.Commands;
using Zentory.Application.Auth.Services;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Apoyo de los tests de emisión de sesión (PLAN-0018 RF-61, RF-85; D-44): el host con FakeGoogleTokenValidator, las
// llamadas a login, Google, refresh y logout, la siembra de refresh tokens y bajas de membresía, y la lectura de la sesión emitida.
public static class AuthSessionApi
{
    public const string LoginEndpoint   = "/api/v1/auth/login";
    public const string GoogleEndpoint  = "/api/v1/auth/google";
    public const string RefreshEndpoint = "/api/v1/auth/refresh";
    public const string LogoutEndpoint  = "/api/v1/auth/logout";
    public const string SwitchEndpoint  = "/api/v1/organization/switch";
    public const string ProfileEndpoint = "/api/v1/me";
    public const string MyOrgsEndpoint  = "/api/v1/me/organizations";

    // Claims de la organización activa que la sesión sin organización no lleva (D-44).
    public static readonly string[] OrganizationClaims = ["active_org_id", "active_org_role", "plan", "legal_type"];

    // Host de la factory con IGoogleTokenValidator reemplazado por el doble; comparte la base InMemory de la factory.
    public static WebApplicationFactory<Program> WithGoogle(ZentoryApiFactory factory, FakeGoogleTokenValidator google)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(google);
        }));

    public static Task<HttpResponseMessage> LoginAsync(HttpClient http, string email, string password)
        => http.PostAsJsonAsync(LoginEndpoint, new { email, password });

    // Id token de Google registrado en el doble para ese sub y ese email.
    public static Task<HttpResponseMessage> GoogleAsync(
        HttpClient http, FakeGoogleTokenValidator google, string subject, User user)
    {
        var idToken = google.Register(new GoogleUserInfo(subject, user.Email, user.FirstName, user.LastName, EmailVerified: true));
        return http.PostAsJsonAsync(GoogleEndpoint, new { idToken });
    }

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient http, string refreshToken, Guid? activeOrgId = null)
        => http.PostAsJsonAsync(RefreshEndpoint, new { refreshToken, activeOrgId });

    public static Task<HttpResponseMessage> LogoutAsync(HttpClient http, string? refreshToken)
        => http.PostAsJsonAsync(LogoutEndpoint, new { refreshToken });

    public static Task<HttpResponseMessage> SwitchAsync(HttpClient http, Guid organizationId)
        => http.PostAsJsonAsync(SwitchEndpoint, new { orgId = organizationId });

    public static HttpClient Bearer(WebApplicationFactory<Program> host, string accessToken)
    {
        var http = host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return http;
    }

    // Refresh token vigente del usuario, sembrado fuera de una petición, en su sesión vigente (RF-90, D-47): si el usuario
    // aún no tiene sesión, se le inicia una, como lo haría un login. Con sessionId, el token queda en esa sesión sin
    // tocar la vigente (p. ej. una sesión ya reemplazada).
    public static async Task<string> SeedRefreshTokenAsync(IServiceProvider services, User user, Guid? sessionId = null)
    {
        var token = $"test-refresh-{Guid.NewGuid():N}";
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var stored = await db.Users.SingleAsync(u => u.UserId == user.UserId);
        var session = sessionId ?? stored.CurrentSessionId ?? stored.StartSession();
        db.RefreshTokens.Add(RefreshToken.Create(user.UserId, token, DateTime.UtcNow.AddDays(1), session));
        await db.SaveChangesAsync();
        return token;
    }

    // Inicia una sesión del usuario fuera de una petición, como lo haría un login sin emitir tokens (RF-90 b), y devuelve
    // su sid: para firmar un access token de esa sesión.
    public static async Task<Guid> StartSessionAsync(IServiceProvider services, User user)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var stored = await db.Users.SingleAsync(u => u.UserId == user.UserId);
        var sessionId = stored.StartSession();
        await db.SaveChangesAsync();
        return sessionId;
    }

    // Sesión vigente del usuario en la base (users.current_session_id).
    public static async Task<Guid?> CurrentSessionIdAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.CurrentSessionId)
            .SingleAsync();
    }

    // Refresh tokens del usuario en la base, sin seguimiento.
    public static async Task<List<RefreshToken>> RefreshTokensOfAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.RefreshTokens.AsNoTracking().Where(rt => rt.UserId == userId).ToListAsync();
    }

    // Fila del refresh token en claro, sin seguimiento.
    public static async Task<RefreshToken> RefreshTokenRowAsync(IServiceProvider services, string refreshToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var hash = RefreshToken.HashOf(refreshToken);
        return await db.RefreshTokens.AsNoTracking().SingleAsync(rt => rt.TokenHash == hash);
    }

    // RegisterCommand y el alta con Google cuelgan la suscripción del plan del catálogo, que la factory no siembra.
    public static async Task EnsureBillingPlanAsync(IServiceProvider services, string plan)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        if (await db.BillingPlans.AnyAsync(p => p.Name == plan)) return;
        db.BillingPlans.Add(BillingPlan.Create(plan, plan, 0m, 0m));
        await db.SaveChangesAsync();
    }

    // Baja de la membresía fuera de una petición, como la haría el DELETE de miembros.
    public static async Task RemoveMembershipAsync(IServiceProvider services, Guid organizationId, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var membership = await db.OrganizationMembers
            .SingleAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.DeletedAt == null);
        membership.SoftDelete();
        await db.SaveChangesAsync();
    }

    // Sesión de una respuesta 200 de login, Google o refresh (o 201 de registro, con expected).
    public static async Task<IssuedSession> ReadSessionAsync(
        HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        var user = root.GetProperty("user");
        return new IssuedSession(
            new JwtSecurityTokenHandler().ReadJwtToken(root.GetProperty("accessToken").GetString()),
            root.GetProperty("accessToken").GetString()!,
            root.GetProperty("refreshToken").GetString()!,
            user.GetProperty("activeOrgId").GetString(),
            root.GetProperty("memberships").GetArrayLength());
    }

    // Error genérico de RF-61 (SR-0018.18) en login y Google: 422, nunca 500, con el cuerpo de las credenciales inválidas.
    public static async Task AssertGenericErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = Assert.Single(body.RootElement.GetProperty("details").EnumerateArray());
        Assert.Equal(field, detail.GetProperty("field").GetString());
    }

    // Refresh rechazado (RF-30 c, RF-61): 401 { error: "unauthorized", code: "REFRESH_TOKEN_INVALID" }, nunca 500. Es el
    // mismo cuerpo para un token inexistente, expirado, revocado o reutilizado y para un usuario sin membresías.
    public static async Task AssertRefreshTokenInvalidAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(RefreshTokenInvalidCode, body.RootElement.GetProperty("code").GetString());
    }

    // sid del access token (RF-90, D-47), leído del JWT sin validar.
    public static Guid SessionIdOf(string accessToken)
        => Guid.ParseExact(ClaimOf(accessToken, SidClaim) ?? throw new InvalidOperationException("Token sin sid."), "D");

    public const string SidClaim = JwtRegisteredClaimNames.Sid;

    public static string? ClaimOf(string accessToken, string type)
        => new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.FirstOrDefault(c => c.Type == type)?.Value;

    public static string CredentialsField => AuthCredentialErrors.Field;
    public const string RefreshTokenInvalidCode = RefreshTokenErrors.InvalidCode;

    // Sesión reemplazada (RF-90 e, RNF-10): 401 con el cuerpo exacto, sin message ni details.
    public const string SessionReplacedBody = "{\"error\":\"unauthorized\",\"code\":\"SESSION_REPLACED\"}";

    public static async Task AssertSessionReplacedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SessionReplacedBody, await response.Content.ReadAsStringAsync());
    }
}

// Decorador del repositorio para simultaneidades deterministas (PLAN-0018 SR-0018.13, RF-90 g; R-28): ejecuta una
// operación competidora completa justo después de leer un token (afterRead) o justo antes de marcarlo para guardar
// (beforeUpdate). La operación detenida sigue con lo que leyó; al guardar, el token de concurrencia IsRevoked ya no
// coincide y recibe DbUpdateConcurrencyException, como con el UPDATE ... WHERE is_revoked = false de PostgreSQL.
public sealed class InterleavingRefreshTokenRepository(
    IRefreshTokenRepository  inner,
    Func<string, Task>?      afterRead    = null,
    Func<RefreshToken, Task>? beforeUpdate = null) : IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var token = await inner.GetByHashAsync(tokenHash, ct);
        if (afterRead is not null)
            await afterRead(tokenHash);
        return token;
    }

    public Task AddAsync(RefreshToken refreshToken, CancellationToken ct = default) => inner.AddAsync(refreshToken, ct);

    public async Task UpdateAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        if (beforeUpdate is not null)
            await beforeUpdate(refreshToken);
        await inner.UpdateAsync(refreshToken, ct);
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default) => inner.RevokeAllForUserAsync(userId, ct);

    public Task RevokeAllForSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
        => inner.RevokeAllForSessionAsync(userId, sessionId, ct);
}

public sealed record IssuedSession(
    JwtSecurityToken AccessToken, string RawAccessToken, string RefreshToken, string? ActiveOrgId, int MembershipCount)
{
    public string? Claim(string type) => AccessToken.Claims.FirstOrDefault(c => c.Type == type)?.Value;
}

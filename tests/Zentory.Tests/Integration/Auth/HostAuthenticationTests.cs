using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Zentory.Application.CollaboratorPortal;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0001 (RF-18 a, b, c; ADR-0001 §Decisión 3 y 4): la cadena real JwtBearer + [Authorize] + TenantContext del host,
// siempre sobre el mismo GET autenticado. Cada test crea sus propios datos (DevSeed apagado en la factory), con ids
// nuevos, así comparten la factory de la clase sin pisarse.
public sealed class HostAuthenticationTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ProfileEndpoint = "/api/v1/organization/profile";
    private const string OwnerRole       = OrgRole.Owner;

    // Clave ajena del CA-28 (de TestJwtSettings): misma forma y longitud que las de prueba (>= 32 bytes) pero distinta
    // de Jwt:Key, así el 401 solo puede venir de la firma.
    private const string ForeignKey = TestJwtSettings.ForeignKey;

    [Fact]
    public async Task PLAN0001_CA16_OrganizationProfile_SinToken_Devuelve401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ProfileEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0001_CA17_OrganizationProfile_ConTokenValido_Devuelve2xx()
    {
        var (user, organization) = await SeedOwnerAsync();
        var token = factory.Tokens.ForUser(user, organization, OwnerRole, Plan.Free);

        var response = await GetProfileAsync(token);

        await AssertProfileOfAsync(response, organization);
    }

    [Fact]
    public async Task PLAN0001_CA28_Jwt_ClaveFactory_Devuelve2xx()
    {
        Assert.True(Encoding.UTF8.GetByteCount(TestJwtSettings.Key) >= 32);
        var (user, organization) = await SeedOwnerAsync();
        var sessionId = await AuthSessionApi.StartSessionAsync(factory.Services, user);
        var token = SignOutsideHost(user, organization, sessionId, TestJwtSettings.Key);

        var response = await GetProfileAsync(token);

        await AssertProfileOfAsync(response, organization);
    }

    [Fact]
    public async Task PLAN0001_CA28_Jwt_ClaveAjena_Devuelve401()
    {
        Assert.True(Encoding.UTF8.GetByteCount(ForeignKey) >= 32);
        Assert.NotEqual(TestJwtSettings.Key, ForeignKey);
        Assert.NotEqual(TestJwtSettings.CollaboratorKey, ForeignKey);
        var (user, organization) = await SeedOwnerAsync();
        var sessionId = await AuthSessionApi.StartSessionAsync(factory.Services, user);
        var token = SignOutsideHost(user, organization, sessionId, ForeignKey);

        var response = await GetProfileAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0001_CA32_TokenColaborador_EnEndpointUsuario_Devuelve401()
    {
        var (_, organization) = await SeedOwnerAsync();
        var collaboratorId = Guid.NewGuid();
        var token = factory.Tokens.ForCollaborator(
            $"collaborator-{Guid.NewGuid():N}@zentory.test",
            [collaboratorId],
            collaboratorId,
            organization.OrganizationId,
            organization.Name,
            CollaboratorAuthMethod.MagicLink);

        var response = await GetProfileAsync(token);

        // El token está bien formado y vigente, con issuer y audience de la factory: el 401 solo puede venir de que
        // el esquema de usuario valida con Jwt:Key y el token va firmado con Jwt:CollaboratorKey.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(User User, Organization Organization)> SeedOwnerAsync()
    {
        var organization = Organization.Create($"Auth tests {Guid.NewGuid():N}", LegalType.Freelance);
        var user = User.Create($"owner-{Guid.NewGuid():N}@zentory.test", "Ana", "Prueba", OwnerRole,
            organizationId: organization.OrganizationId);
        organization.SetOwner(user.UserId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Organizations.Add(organization);
        db.Users.Add(user);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, user.UserId, OwnerRole));
        await db.SaveChangesAsync();

        return (user, organization);
    }

    private async Task<HttpResponseMessage> GetProfileAsync(string token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    // El 2xx se comprueba junto con el cuerpo: el perfil devuelto es el de la organización del claim active_org_id,
    // así queda probado que TenantContext leyó el token validado y no solo que la petición pasó.
    private static async Task AssertProfileOfAsync(HttpResponseMessage response, Organization organization)
    {
        Assert.True(response.IsSuccessStatusCode, $"Se esperaba 2xx y llegó {(int)response.StatusCode}.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(organization.OrganizationId, body.GetProperty("id").GetGuid());
        Assert.Equal(organization.Name, body.GetProperty("name").GetString());
    }

    // Réplica fuera del host de lo que firma JwtService (mismos claims, issuer, audience y algoritmo): lo único que
    // varía entre los dos tests del CA-28 es la clave. Lleva el sid de la sesión vigente del usuario (PLAN-0018 RF-90 d).
    private static string SignOutsideHost(User user, Organization organization, Guid sessionId, string signingKey)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Sid,   sessionId.ToString("D")),
            new Claim("active_org_id",   organization.OrganizationId.ToString()),
            new Claim("active_org_role", OwnerRole),
            new Claim("plan",            Plan.Free),
            new Claim("legal_type",      organization.LegalType),
            new Claim("role",            user.Role),
            new Claim("initials",        "AP"),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             TestJwtSettings.Issuer,
            audience:           TestJwtSettings.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

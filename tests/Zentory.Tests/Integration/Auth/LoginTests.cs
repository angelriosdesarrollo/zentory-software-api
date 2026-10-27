using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T17, CA-26 (RF-61; SR-2.13, D-10) sobre el host real: POST /auth/login con credenciales correctas responde
// 200 con access y refresh token; contraseña incorrecta, email inexistente y cuenta solo de Google responden el mismo
// error genérico; el email se normaliza (espacios y mayúsculas) antes de buscar la cuenta.
public sealed class LoginTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword = "Owner-Login-2026!";

    [Fact]
    public async Task PLAN0018_CA26_LoginConCredencialesCorrectas_Responde200ConAccessYRefreshToken()
    {
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(OwnerPassword)).BuildAsync();
        using var http = factory.CreateClient();

        using var response = await LoginAsync(http, tenants.A.Owner.Email, OwnerPassword);

        var session = await ReadSessionAsync(response);
        Assert.False(string.IsNullOrEmpty(session.RawAccessToken));
        Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        Assert.Equal(tenants.A.OrganizationId.ToString(), session.ActiveOrgId);
    }

    [Fact]
    public async Task PLAN0018_CA26_ContrasenaIncorrectaEmailInexistenteYCuentaSoloGoogle_RespondenElMismoErrorGenerico()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithOwnerPassword(OwnerPassword))
            .ForB(b => b.WithOwnerGoogleId($"google-{Guid.NewGuid():N}"))
            .BuildAsync();
        Assert.Null(tenants.B.Owner.PasswordHash);
        using var http = factory.CreateClient();

        using var wrongPassword = await LoginAsync(http, tenants.A.Owner.Email, OwnerPassword + "-mal");
        using var unknownEmail  = await LoginAsync(http, $"nadie-{Guid.NewGuid():N}@zentory.test", OwnerPassword);
        using var googleOnly    = await LoginAsync(http, tenants.B.Owner.Email, OwnerPassword);

        await AssertGenericErrorAsync(wrongPassword, CredentialsField);
        var expected = await wrongPassword.Content.ReadAsStringAsync();
        Assert.Equal(expected, await unknownEmail.Content.ReadAsStringAsync());
        Assert.Equal(expected, await googleOnly.Content.ReadAsStringAsync());
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, googleOnly.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA26_LoginConEmailConEspaciosYMayusculas_EntraALaCuentaNormalizada()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var ana = User.Create("ana@example.com", "Ana", "Prueba", OrgRole.Member,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(OwnerPassword, 4),
            organizationId: tenants.A.OrganizationId);
        await tenants.SeedAsync(ana,
            OrganizationMember.Create(tenants.A.OrganizationId, ana.UserId, OrgRole.Member, invitedBy: tenants.A.Owner.UserId));
        using var http = factory.CreateClient();

        using var response = await LoginAsync(http, "  Ana@Example.COM ", OwnerPassword);

        var session = await ReadSessionAsync(response);
        Assert.Equal(ana.UserId.ToString(), session.Claim("sub"));
        Assert.Equal("ana@example.com", session.Claim("email"));
        Assert.Equal(tenants.A.OrganizationId.ToString(), session.ActiveOrgId);
    }
}

using Microsoft.AspNetCore.Mvc.Testing;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 T11b, CA-83 (RF-61; SR-0018.18) sobre el host real: el usuario cuya única membresía se borró, sin
// organizaciones propias restaurables, recibe en login, Google y refresh el 4xx genérico, nunca 500. El cuerpo es idéntico
// al de una contraseña incorrecta (login y Google: 422) o al de un refresh token desconocido (refresh: 401
// REFRESH_TOKEN_INVALID, RF-30 c): no revela que la cuenta existe.
public sealed class NoMembershipLoginTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string MemberPassword = "Miembro-Sin-Membresia-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public NoMembershipLoginTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = WithGoogle(factory, _google);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA83_LoginConMembresiaUnicaBorrada_Responde422GenericoIgualAContrasenaIncorrecta()
    {
        var seed = await SeedMemberWithoutMembershipAsync();
        using var http = _host.CreateClient();

        using var response      = await LoginAsync(http, seed.Member.Email, MemberPassword);
        using var wrongPassword = await LoginAsync(http, seed.Member.Email, MemberPassword + "-mal");

        await AssertGenericErrorAsync(response, CredentialsField);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PLAN0018_CA83_GoogleConMembresiaUnicaBorrada_Responde422GenericoNunca500()
    {
        var seed = await SeedMemberWithoutMembershipAsync();
        using var http = _host.CreateClient();

        // Primer ingreso con Google de una cuenta existente: se vincula por email y no tiene desde dónde emitir sesión.
        using var response      = await GoogleAsync(http, _google, $"google-{Guid.NewGuid():N}", seed.Member);
        using var wrongPassword = await LoginAsync(http, seed.Member.Email, MemberPassword + "-mal");

        await AssertGenericErrorAsync(response, CredentialsField);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PLAN0018_CA83_RefreshConMembresiaUnicaBorrada_Responde401RefreshTokenInvalidIgualATokenDesconocido()
    {
        var seed = await SeedMemberWithoutMembershipAsync();
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, seed.Member);
        using var http = _host.CreateClient();

        using var response = await RefreshAsync(http, refreshToken, seed.OrganizationId);
        using var unknown  = await RefreshAsync(http, $"desconocido-{Guid.NewGuid():N}");

        // Desde T17 (RF-30 c) el refresh inválido es 401 REFRESH_TOKEN_INVALID; sin membresías responde exactamente eso.
        await AssertRefreshTokenInvalidAsync(response);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PLAN0018_CA83_LoginConMembresiaVigente_Responde200ConLaOrganizacion()
    {
        // Control positivo: con la membresía vigente, las mismas credenciales entran a la organización.
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithMember(OrgRole.Member).WithMemberPassword(MemberPassword))
            .BuildAsync();
        using var http = _host.CreateClient();

        using var response = await LoginAsync(http, tenants.A.Member(OrgRole.Member).Email, MemberPassword);

        var session = await ReadSessionAsync(response);
        Assert.Equal(tenants.A.OrganizationId.ToString(), session.ActiveOrgId);
    }

    private sealed record MemberSeed(Guid OrganizationId, User Member);

    // Member de A (con contraseña) cuya única membresía se borró; no es owner de ninguna organización.
    private async Task<MemberSeed> SeedMemberWithoutMembershipAsync()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithMember(OrgRole.Member).WithMemberPassword(MemberPassword))
            .BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        await RemoveMembershipAsync(_host.Services, tenants.A.OrganizationId, member.UserId);
        return new MemberSeed(tenants.A.OrganizationId, member);
    }
}

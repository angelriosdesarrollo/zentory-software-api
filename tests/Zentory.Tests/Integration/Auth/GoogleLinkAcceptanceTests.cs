using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 CA-28 (RF-28; SR-0018.15), pruebas de aceptación agregadas por QA en TK-0050: la revocación al vincular una
// cuenta sin verificar no alcanza a otros usuarios, y el 422 genérico de un usuario sin membresías (RF-61) no deja
// guardado el vínculo ni la anulación de la contraseña ni la revocación de sus sesiones.
public sealed class GoogleLinkAcceptanceTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Password = "Qa-Google-Link-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public GoogleLinkAcceptanceTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host    = WithGoogle(factory, _google);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA28_Google_VincularCuentaSinVerificar_NoRevocaLosRefreshTokensDeOtrosUsuarios()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerPassword(Password))
            .ForB(b => b.WithOwnerPassword(Password))
            .BuildAsync();
        var linked = tenants.A.Owner;
        var other  = tenants.B.Owner;
        var linkedToken = await SeedRefreshTokenAsync(_host.Services, linked);
        var otherToken  = await SeedRefreshTokenAsync(_host.Services, other);

        using var response = await GoogleAsync(_host.CreateClient(), _google, $"google-{Guid.NewGuid():N}", linked);

        await ReadSessionAsync(response);
        Assert.True(await IsRevokedAsync(linkedToken));
        Assert.False(await IsRevokedAsync(otherToken));
    }

    [Fact]
    public async Task PLAN0018_CA28_Google_SinMembresiasConError422Generico_NoGuardaVinculoNiContrasenaNiRevocacion()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithMember(OrgRole.Member).WithMemberPassword(Password))
            .BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        await RemoveMembershipAsync(_host.Services, tenants.A.OrganizationId, member.UserId);
        var before = await GetUserAsync(member.UserId);
        Assert.Null(before.EmailVerifiedAt);
        var refreshToken = await SeedRefreshTokenAsync(_host.Services, member);

        using var response = await GoogleAsync(_host.CreateClient(), _google, $"google-{Guid.NewGuid():N}", member);

        await AssertGenericErrorAsync(response, CredentialsField);
        var stored = await GetUserAsync(member.UserId);
        Assert.Null(stored.GoogleId);
        Assert.Equal(before.PasswordHash, stored.PasswordHash);
        Assert.Null(stored.EmailVerifiedAt);
        Assert.False(await IsRevokedAsync(refreshToken));
    }

    private async Task<User> GetUserAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.UserId == userId);
    }

    private async Task<bool> IsRevokedAsync(string rawRefreshToken)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var hash = RefreshToken.HashOf(rawRefreshToken);
        var token = await db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.TokenHash == hash);
        return token.IsRevoked;
    }
}

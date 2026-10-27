using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-114 (punto Google de la aceptación; RF-81 c, RF-84) con un destino solo con Google, el doble del validador
// y el reloj simulado: acepta con un id_token reciente que lleva el nonce de organization.transfer.accept, y no con el de
// organization.transfer ni el de organization.delete.
public sealed class AcceptOwnershipTransferGoogleTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Aceptar-Google-2026!";
    private const string MemberPassword = "Miembro-Aceptar-Google-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeTimeProvider               _time;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public AcceptOwnershipTransferGoogleTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_time);
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(_google);
        }));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA114_Aceptar_DestinoSoloGoogleConElNonceDeTransferAccept_Responde204()
    {
        var (tenants, target) = await SeedGoogleTargetAsync();
        using var ownerHttp  = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        using var targetHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, target);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);
        var nonce = await OrganizationDeletionApi.NonceAsync(targetHttp, ReauthAction.OrganizationTransferAccept);

        using var response = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId,
            googleIdToken: Token(target, nonce, TimeSpan.FromMinutes(-4)));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(target.UserId, (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
        Assert.True(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));
        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    [Theory]
    [InlineData(ReauthAction.OrganizationTransfer)]
    [InlineData(ReauthAction.OrganizationDelete)]
    public async Task PLAN0018_CA114_Aceptar_DestinoSoloGoogleConElNonceDeOtraAccion_Responde403SinCambios(string action)
    {
        var (tenants, target) = await SeedGoogleTargetAsync();
        using var ownerHttp  = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        using var targetHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, target);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);
        var nonce = await OrganizationDeletionApi.NonceAsync(targetHttp, action);

        using var response = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId, googleIdToken: Token(target, nonce));

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(response);
        Assert.Equal(tenants.A.Owner.UserId,
            (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
    }

    [Fact]
    public async Task PLAN0018_CA114_Aceptar_DestinoSoloGoogleConIdTokenDeHaceMasDe5Min_Responde403SinCambios()
    {
        var (tenants, target) = await SeedGoogleTargetAsync();
        using var ownerHttp  = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        using var targetHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, target);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);
        var nonce = await OrganizationDeletionApi.NonceAsync(targetHttp, ReauthAction.OrganizationTransferAccept);

        using var response = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId,
            googleIdToken: Token(target, nonce, -(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1))));

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(response);
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
        Assert.False(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // El admin de A pasa a ser un usuario solo con Google: sin contraseña y con GoogleId.
    private async Task<(TenantPair Tenants, User Target)> SeedGoogleTargetAsync()
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword);
        var target = tenants.A.Member(OrgRole.Admin);
        var googleId = $"google-sub-{Guid.NewGuid():N}";
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            var stored = await db.Users.FindAsync(target.UserId);
            stored!.LinkGoogleAccount(googleId);
            stored.ClearPassword();
            await db.SaveChangesAsync();
        }
        target.LinkGoogleAccount(googleId);
        return (tenants, target);
    }

    // id_token válido por defecto: sub del destino, email verificado, iat = ahora del reloj simulado.
    private string Token(User target, string? nonce, TimeSpan? iatOffset = null)
        => _google.Register(new GoogleUserInfo(target.GoogleId!, target.Email, target.FirstName, target.LastName,
            EmailVerified: true, IssuedAt: _time.GetUtcNow() + (iatOffset ?? TimeSpan.Zero), Nonce: nonce));
}

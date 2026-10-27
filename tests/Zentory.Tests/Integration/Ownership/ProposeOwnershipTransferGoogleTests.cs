using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 T30b con un owner solo con Google, el doble del validador y el reloj simulado: CA-T30B-1 (parte de CA-114,
// Google: id_token reciente con el nonce de organization.transfer) y la parte de CA-117 de la transferencia (un segundo uso
// del mismo nonce deja la propuesta tal como la dejó la primera).
public sealed class ProposeOwnershipTransferGoogleTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string MemberPassword = "Miembro-Transferir-Google-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeTimeProvider               _time;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public ProposeOwnershipTransferGoogleTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        // Segundo exacto y cerca del reloj real: el JWT de la petición se emite con la hora del sistema.
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
    public async Task PLAN0018_CA114_Proponer_OwnerSoloGoogleConIdTokenDeHace4MinYNonceDeTransfer_Responde202()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationTransfer);

        using var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId,
            googleIdToken: Token(owner, nonce, iatOffset: TimeSpan.FromMinutes(-4)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var transfer = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(OwnershipTransferStatus.Pending, transfer.Status);
        // ExpiresAt sale del TimeProvider del host (RF-64 c).
        Assert.Equal(_time.GetUtcNow().UtcDateTime + OrgSecurity.OwnershipTransferLifetime, transfer.ExpiresAt);
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_OwnerSoloGoogleConIdTokenDeHaceMasDe5Min_Responde403SinPropuesta()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationTransfer);

        using var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId,
            googleIdToken: Token(owner, nonce, iatOffset: -(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1))));

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(response);
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_ConElNonceDeOrganizationDelete_Responde403SinPropuesta()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants);
        var deleteNonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationDelete);

        using var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId,
            googleIdToken: Token(owner, deleteNonce));

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(response);
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA117_Proponer_SegundaPropuestaConElMismoNonce_Responde403YDejaLaPendienteComoLaDejoLaPrimera()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var admin  = tenants.A.Member(OrgRole.Admin);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = Client(tenants);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationTransfer);

        using (var first = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId,
                   googleIdToken: Token(owner, nonce)))
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var afterFirst = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));

        // Dentro de sus 5 minutos, con un id_token nuevo que lleva el mismo nonce y hacia otro miembro: la reautenticación
        // va antes que TRANSFER_PENDING (RF-81 e), así que responde 403 sin tocar la pendiente.
        _time.Advance(TimeSpan.FromMinutes(1));
        using var reuse = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, member.UserId,
            googleIdToken: Token(owner, nonce));

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(reuse);
        var afterSecond = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(
            (afterFirst.Id, afterFirst.ToUserId, afterFirst.Status, afterFirst.PreviewHash, afterFirst.ExpiresAt, afterFirst.UpdatedAt),
            (afterSecond.Id, afterSecond.ToUserId, afterSecond.Status, afterSecond.PreviewHash, afterSecond.ExpiresAt, afterSecond.UpdatedAt));
        Assert.Equal(admin.UserId, afterSecond.ToUserId);
    }

    [Fact]
    public async Task PLAN0018_CA117_Proponer_AcuseVencidoNoConsumeElNonce_ElReintentoCorrectoResponde202()
    {
        var (tenants, owner) = await SeedGoogleOwnerAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants);
        var nonce = await OrganizationDeletionApi.NonceAsync(http, ReauthAction.OrganizationTransfer);
        var idToken = Token(owner, nonce);

        using (var stale = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, new string('0', 64), googleIdToken: idToken))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));

        using var retry = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId, googleIdToken: idToken);

        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.True(_host.Services.GetRequiredService<IReauthNonceStore>().IsConsumed(nonce));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(TenantPair Tenants, User Owner)> SeedGoogleOwnerAsync()
    {
        await DemoScenarioSeed.EnsureAsync(_factory.Services);
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerGoogleId($"google-sub-{Guid.NewGuid():N}")
                .WithMember(OrgRole.Admin).WithMember(OrgRole.Member).WithMemberPassword(MemberPassword))
            .BuildAsync();
        await OwnershipTransferApi.MarkEmailVerifiedAsync(_host.Services, tenants.A.Members.Select(m => m.User).ToArray());
        return (tenants, tenants.A.Owner);
    }

    private HttpClient Client(TenantPair tenants) => OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

    // id_token válido por defecto: sub del owner, email verificado, iat = ahora del reloj simulado.
    private string Token(User owner, string? nonce, TimeSpan? iatOffset = null)
        => _google.Register(new GoogleUserInfo(owner.GoogleId!, owner.Email, owner.FirstName, owner.LastName,
            EmailVerified: true, IssuedAt: _time.GetUtcNow() + (iatOffset ?? TimeSpan.Zero), Nonce: nonce));
}

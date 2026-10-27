using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-T30B-2 (parte de CA-127; RF-64 c, c2, c3; RF-83 h) sobre el host real con el reloj simulado: cancelar la
// propuesta (204 idempotente), una sola pendiente por organización (409 TRANSFER_PENDING), la vencida no bloquea, y
// eliminar la organización la cancela sin que restaurar la reactive.
public sealed class CancelOwnershipTransferTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Cancelar-2026!";
    private const string MemberPassword = "Miembro-Cancelar-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeTimeProvider               _time;
    private readonly WebApplicationFactory<Program> _host;

    public CancelOwnershipTransferTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_time);
        }));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA127_Cancelar_DelOwner_Responde204CancelledRepetirloSinEfectoYLuegoPuedeProponerDeNuevo()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        await ProposeAsync(http, admin);

        using (var cancel = await OwnershipTransferApi.CancelAsync(http))
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        var cancelled = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(OwnershipTransferStatus.Cancelled, cancelled.Status);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, cancelled.ResolvedAt);

        // Repetirlo: 204 sin efecto (ni estado ni ActivityLog).
        var activity = await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId);
        using (var again = await OwnershipTransferApi.CancelAsync(http))
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var unchanged = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal((cancelled.Status, cancelled.ResolvedAt, cancelled.UpdatedAt), (unchanged.Status, unchanged.ResolvedAt, unchanged.UpdatedAt));
        Assert.Equal(activity, await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId));

        // Tras cancelar, O puede proponer de nuevo.
        await ProposeAsync(http, admin);
        var transfers = await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal(2, transfers.Count);
        Assert.Single(transfers, t => t.Status == OwnershipTransferStatus.Pending);
    }

    [Fact]
    public async Task PLAN0018_CA127_Cancelar_DejaActivityLogYNoCambiaOwnerNiRoles()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        await ProposeAsync(http, admin);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);
        var activity = await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId);

        using (var cancel = await OwnershipTransferApi.CancelAsync(http))
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        Assert.Equal(activity + 1, await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(roles, await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(tenants.A.Owner.UserId,
            (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
    }

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA127_Cancelar_DelAdminODelMember_Responde403AccessDeniedYLaPropuestaSiguePendiente(string role)
    {
        var tenants = await SeedAsync();
        using var owner = Client(tenants, tenants.A);
        await ProposeAsync(owner, tenants.A.Member(OrgRole.Admin));
        using var http = Client(tenants, tenants.A, tenants.A.Member(role));

        using var response = await OwnershipTransferApi.CancelAsync(http);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(OwnershipTransferStatus.Pending,
            Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId)).Status);
    }

    [Fact]
    public async Task PLAN0018_CA127_Proponer_ConUnaVigenteHaciaA_HaciaOtroMiembroResponde409TransferPendingSinEfecto()
    {
        var tenants = await SeedAsync();
        var admin  = tenants.A.Member(OrgRole.Admin);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = Client(tenants, tenants.A);
        await ProposeAsync(http, admin);
        var activity = await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId);

        using var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, member.UserId, OwnerPassword);

        await OwnershipTransferApi.AssertConflictAsync(response, ProposeOwnershipTransferCommandHandler.PendingCode);
        var transfer = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal((admin.UserId, OwnershipTransferStatus.Pending), (transfer.ToUserId, transfer.Status));
        Assert.Equal(activity, await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA127_Proponer_ConUnaPendienteVencida_NoBloqueaYLaVencidaPasaACancelled()
    {
        var tenants = await SeedAsync();
        var admin  = tenants.A.Member(OrgRole.Admin);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = Client(tenants, tenants.A);
        await ProposeAsync(http, admin);

        _time.Advance(OrgSecurity.OwnershipTransferLifetime + TimeSpan.FromSeconds(1));
        await ProposeAsync(http, member);

        var transfers = await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal(2, transfers.Count);
        var expired = Assert.Single(transfers, t => t.ToUserId == admin.UserId);
        Assert.Equal(OwnershipTransferStatus.Cancelled, expired.Status);
        var current = Assert.Single(transfers, t => t.ToUserId == member.UserId);
        Assert.Equal(OwnershipTransferStatus.Pending, current.Status);
    }

    [Fact]
    public async Task PLAN0018_CA127_EliminarLaOrganizacion_CancelaLaPendienteYRestaurarNoLaReactiva()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        await ProposeAsync(http, admin);

        using (var delete = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var cancelled = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(OwnershipTransferStatus.Cancelled, cancelled.Status);

        using (var restore = await OrganizationRestoreApi.RestoreAsync(http, tenants.A.OrganizationId))
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).DeletedAt);
        var afterRestore = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(OwnershipTransferStatus.Cancelled, afterRestore.Status);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<TenantPair> SeedAsync()
        => OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword);

    private HttpClient Client(TenantPair tenants, SeededTenant tenant, User? user = null)
        => OrganizationDeletionApi.ClientFor(_host, tenants, tenant, user);

    private static async Task ProposeAsync(HttpClient http, User target)
    {
        using var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, target.UserId, OwnerPassword);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}

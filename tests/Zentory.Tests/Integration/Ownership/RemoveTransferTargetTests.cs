using System.Net;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-56, punto RF-83 h, sobre el host real: dar de baja al destino de la transferencia pendiente (owner o admin)
// cancela la propuesta en la misma escritura; dar de baja a otro miembro no la toca.
public sealed class RemoveTransferTargetTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword  = "Owner-Baja-Destino-2026!";
    private const string MemberPassword = "Miembro-Baja-Destino-2026!";

    [Fact]
    public async Task PLAN0018_CA56_BajaDelDestinoPorElOwner_Responde204YLaPropuestaQuedaCancelled()
    {
        var tenants = await SeedAsync();
        var target = tenants.A.Member(OrgRole.Member);
        using var owner = tenants.ClientFor(tenants.A);
        await ProposeAsync(owner, target);

        using (var removal = await MemberManagementApi.RemoveAsync(owner, target.UserId))
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        var transfer = Assert.Single(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Equal(OwnershipTransferStatus.Cancelled, transfer.Status);
        Assert.NotNull(transfer.ResolvedAt);
        Assert.DoesNotContain(target.UserId,
            (await OwnershipTransferApi.RolesAsync(factory.Services, tenants.A.OrganizationId)).Keys);
    }

    [Fact]
    public async Task PLAN0018_CA56_BajaDelDestinoPorUnAdmin_CancelaLaPropuesta()
    {
        var tenants = await SeedAsync(extraAdmin: true);
        var admins = tenants.A.Members.Where(m => m.Role == OrgRole.Admin).Select(m => m.User).ToList();
        var (actor, target) = (admins[0], admins[1]);
        using var owner = tenants.ClientFor(tenants.A);
        await ProposeAsync(owner, target);
        using var admin = tenants.ClientFor(tenants.A, actor);

        using (var removal = await MemberManagementApi.RemoveAsync(admin, target.UserId))
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        Assert.Equal(OwnershipTransferStatus.Cancelled,
            Assert.Single(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId)).Status);
    }

    [Fact]
    public async Task PLAN0018_CA56_BajaDeOtroMiembro_NoTocaLaPropuestaPendiente()
    {
        var tenants = await SeedAsync();
        var target = tenants.A.Member(OrgRole.Admin);
        var other  = tenants.A.Member(OrgRole.Member);
        using var owner = tenants.ClientFor(tenants.A);
        await ProposeAsync(owner, target);
        var before = Assert.Single(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));

        using (var removal = await MemberManagementApi.RemoveAsync(owner, other.UserId))
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        var after = Assert.Single(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Equal((before.Status, before.UpdatedAt), (after.Status, after.UpdatedAt));
        Assert.Equal(OwnershipTransferStatus.Pending, after.Status);
    }

    private Task<TenantPair> SeedAsync(bool extraAdmin = false)
        => OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword,
            configureA: a =>
            {
                if (extraAdmin)
                    a.WithMember(OrgRole.Admin);
            });

    private static async Task ProposeAsync(HttpClient http, User target)
    {
        using var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, target.UserId, OwnerPassword);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}

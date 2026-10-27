using System.Net;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.InvitationAcceptApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 T29b, viñeta de CA-103 (RF-65 e, RF-50 g; SR-0018.26 e) sobre el host real: tras eliminar A con
// DELETE /organization, POST /auth/invitations/accept con una invitación vigente de A responde 422 TOKEN_INVALID sin
// cuenta ni membresía, sin JWT y con JWT. La invitación de B, en el mismo escenario, sí se acepta (control).
public sealed class DeletedOrganizationInvitationTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string Password = "Owner-Correcta-Invitacion-2026!";

    [Fact]
    public async Task PLAN0018_CA103_InvitationsAccept_SinJwt_InvitacionVigenteDeOrganizacionEliminada_422TokenInvalidSinCuentaNiMembresia()
    {
        var tenants = await SeedAsync();
        var toA = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("borrada-sin-jwt"),
            OrgRole.Member, tenants.A.Owner.UserId);
        var toB = await SeedInvitationAsync(factory.Services, tenants.B.OrganizationId, NewEmail("vigente-sin-jwt"),
            OrgRole.Member, tenants.B.Owner.UserId);

        await DeleteAAsync(tenants);
        using var http = factory.CreateClient();

        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, toA.RawToken));
        Assert.Null(await UserByEmailAsync(factory.Services, toA.Email));
        Assert.Equal(0, await MembershipCountForEmailAsync(factory.Services, tenants.A.OrganizationId, toA.Email));
        Assert.Null((await InvitationRowAsync(factory.Services, toA.Id)).AcceptedAt);

        using (var control = await AcceptAnonymousAsync(http, toB.RawToken))
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA103_InvitationsAccept_ConJwt_InvitacionVigenteDeOrganizacionEliminada_422TokenInvalidSinMembresia()
    {
        var tenants = await SeedAsync();
        // El invitado es el owner de B, con su email verificado y B activa en su token.
        var invited = tenants.B.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invited.UserId);
        var toA = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, invited.Email, OrgRole.Admin,
            tenants.A.Owner.UserId);

        await DeleteAAsync(tenants);
        using var http = factory.CreateClient();

        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.B), toA.RawToken));

        Assert.DoesNotContain(await MembershipsOfAsync(factory.Services, invited.UserId),
            m => m.OrganizationId == tenants.A.OrganizationId);
        Assert.Null((await InvitationRowAsync(factory.Services, toA.Id)).AcceptedAt);
    }

    private Task<TenantPair> SeedAsync()
        => new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(Password)).BuildAsync();

    private async Task DeleteAAsync(TenantPair tenants)
    {
        using var http = OrganizationDeletionApi.ClientFor(factory, tenants, tenants.A);
        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

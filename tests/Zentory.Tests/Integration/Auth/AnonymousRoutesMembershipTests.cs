using System.Net;
using System.Text.Json;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.InvitationAcceptApi;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 REV-0005 H-4 (RF-14, RF-50, RF-85, RF-30 d): las acciones que no exigen autorización (logout, aceptar una
// invitación) no dependen de la membresía. Con un Bearer válido de una sesión sin organización o de una organización
// eliminada, el middleware las deja pasar: logout revoca el refresh token en el servidor y una persona sin organización
// puede aceptar una invitación. Las acciones autenticadas siguen respondiendo 401 MEMBERSHIP_REVOKED / ORGANIZATION_DELETED.
public sealed class AnonymousRoutesMembershipTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string AuthenticatedEndpoint = "/api/v1/organization/members";

    [Fact]
    public async Task PLAN0018_REV5_H4_Logout_ConTokenSinOrganizacion_Responde204YRevocaElRefreshEnElServidor()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var user = tenants.A.Owner;
        var refresh = await SeedRefreshTokenAsync(factory.Services, user);
        using var http = Bearer(factory, OrganizationRestoreApi.TokenWithoutOrganization(factory.Services, user));

        using var response = await LogoutAsync(http, refresh);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True((await RefreshTokenRowAsync(factory.Services, refresh)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_REV5_H4_Logout_ConTokenDeOrganizacionEliminada_Responde204YRevocaElRefreshEnElServidor()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var user = tenants.A.Owner;
        var refresh = await SeedRefreshTokenAsync(factory.Services, user);
        using var http = Bearer(factory, tenants.TokenFor(tenants.A));
        await OrganizationRestoreApi.MarkDeletedAsync(factory.Services, tenants.A.OrganizationId, user.UserId, DateTime.UtcNow);

        using var response = await LogoutAsync(http, refresh);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True((await RefreshTokenRowAsync(factory.Services, refresh)).IsRevoked);
    }

    [Fact]
    public async Task PLAN0018_REV5_H4_AceptarInvitacion_ConTokenSinOrganizacion_Responde200YCreaLaMembresia()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var invitee = tenants.B.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invitee.UserId);
        var invitation = await SeedInvitationAsync(
            factory.Services, tenants.A.OrganizationId, invitee.Email, OrgRole.Member, tenants.A.Owner.UserId);
        var token = OrganizationRestoreApi.TokenWithoutOrganization(factory.Services, invitee);
        using var http = factory.CreateClient();

        using var response = await AcceptWithBearerAsync(http, token, invitation.RawToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await InvitationRowAsync(factory.Services, invitation.Id)).AcceptedAt);
        Assert.Contains(await MembershipsOfAsync(factory.Services, invitee.UserId),
            m => m.OrganizationId == tenants.A.OrganizationId && m.DeletedAt == null);
    }

    [Fact]
    public async Task PLAN0018_REV5_H4_AceptarInvitacion_ConTokenDeOrganizacionEliminada_Responde200YCreaLaMembresia()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        var invitee = tenants.B.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invitee.UserId);
        var invitation = await SeedInvitationAsync(
            factory.Services, tenants.A.OrganizationId, invitee.Email, OrgRole.Member, tenants.A.Owner.UserId);
        var token = tenants.TokenFor(tenants.B);
        await OrganizationRestoreApi.MarkDeletedAsync(factory.Services, tenants.B.OrganizationId, invitee.UserId, DateTime.UtcNow);
        using var http = factory.CreateClient();

        using var response = await AcceptWithBearerAsync(http, token, invitation.RawToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(await MembershipsOfAsync(factory.Services, invitee.UserId),
            m => m.OrganizationId == tenants.A.OrganizationId && m.DeletedAt == null);
    }

    [Fact]
    public async Task PLAN0018_REV5_H4_AccionAutenticada_SinOrganizacionOConEliminada_SigueRespondiendo401()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        using var withoutOrganization = Bearer(factory, OrganizationRestoreApi.TokenWithoutOrganization(factory.Services, tenants.B.Owner));
        using var deleted = Bearer(factory, tenants.TokenFor(tenants.A));
        await OrganizationRestoreApi.MarkDeletedAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId, DateTime.UtcNow);

        Assert.Equal("MEMBERSHIP_REVOKED", await UnauthorizedCodeAsync(withoutOrganization));
        Assert.Equal("ORGANIZATION_DELETED", await UnauthorizedCodeAsync(deleted));
    }

    private static async Task<string?> UnauthorizedCodeAsync(HttpClient http)
    {
        using var response = await http.GetAsync(AuthenticatedEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}

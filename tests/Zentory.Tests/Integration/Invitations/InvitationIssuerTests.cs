using System.Net;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.AuthSessionApi;
using static Zentory.Tests.Support.InvitationAcceptApi;
using static Zentory.Tests.Support.OneTimeTokenApi;

namespace Zentory.Tests.Integration.Invitations;

// PLAN-0018 REV-0005 H-5 (RF-46, RF-48, RF-50; SR-0018.21): una invitación pendiente deja de ser canjeable cuando quien la
// emitió ya no es owner ni admin vigente de la organización. Se cierra en el origen (la baja y el paso a member revocan sus
// invitaciones pendientes en la misma escritura, sin tocar las de otros emisores) y al canjear (422 TOKEN_INVALID
// uniforme, sin consumir el token ni crear cuenta o membresía), también para invitaciones anteriores a la revocación.
public sealed class InvitationIssuerTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@invitada.zentory.test";

    // ── Origen: baja y degradación revocan ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_REV5_H5_AdminDadoDeBaja_SusInvitacionesPendientesSeRevocan_YNoSePuedenAceptarConNiSinJwt()
    {
        var (tenants, admin, other) = await SeedAsync();
        var byAdmin = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("alterno"), OrgRole.Admin, admin.UserId);
        var byAdminForExisting = await SeedInvitationForExistingUserAsync(tenants, admin);
        var byOther = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("otro"), OrgRole.Admin, other.UserId);
        var byOwner = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("emisor-propietario"), OrgRole.Member, tenants.A.Owner.UserId);

        using (var ownerHttp = tenants.ClientFor(tenants.A))
        using (var removed = await MemberManagementApi.RemoveAsync(ownerHttp, admin.UserId))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        Assert.NotNull((await InvitationRowAsync(factory.Services, byAdmin.Id)).RevokedAt);
        Assert.NotNull((await InvitationRowAsync(factory.Services, byAdminForExisting.Invitation.Id)).RevokedAt);
        Assert.Null((await InvitationRowAsync(factory.Services, byOther.Id)).RevokedAt);
        Assert.Null((await InvitationRowAsync(factory.Services, byOwner.Id)).RevokedAt);

        using var http = factory.CreateClient();
        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, byAdmin.RawToken));
        Assert.Null(await UserByEmailAsync(factory.Services, byAdmin.Email));
        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(http, byAdminForExisting.AccessToken, byAdminForExisting.Invitation.RawToken));
        Assert.Equal(0, await MembershipCountForEmailAsync(factory.Services, tenants.A.OrganizationId, byAdminForExisting.Invitation.Email));

        // Las de otros emisores siguen canjeables.
        using (var accepted = await AcceptAnonymousAsync(http, byOther.RawToken))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using (var acceptedOwner = await AcceptAnonymousAsync(http, byOwner.RawToken))
            Assert.Equal(HttpStatusCode.OK, acceptedOwner.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_REV5_H5_AdminDegradadoAMember_SusInvitacionesPendientesSeRevocan_YNoSePuedenAceptar()
    {
        var (tenants, admin, other) = await SeedAsync();
        var byAdmin = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("alterno"), OrgRole.Admin, admin.UserId);
        var byAdminForExisting = await SeedInvitationForExistingUserAsync(tenants, admin);
        var byOther = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("otro"), OrgRole.Admin, other.UserId);

        using (var ownerHttp = tenants.ClientFor(tenants.A))
        using (var demoted = await MemberManagementApi.ChangeRoleAsync(ownerHttp, admin.UserId, OrgRole.Member))
            Assert.Equal(HttpStatusCode.NoContent, demoted.StatusCode);

        Assert.NotNull((await InvitationRowAsync(factory.Services, byAdmin.Id)).RevokedAt);
        Assert.Null((await InvitationRowAsync(factory.Services, byOther.Id)).RevokedAt);

        using var http = factory.CreateClient();
        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, byAdmin.RawToken));
        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(http, byAdminForExisting.AccessToken, byAdminForExisting.Invitation.RawToken));
        using (var accepted = await AcceptAnonymousAsync(http, byOther.RawToken))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_REV5_H5_AdminQueSigueSiendoAdmin_ElCambioDeRolNoRevocaSusInvitaciones()
    {
        var (tenants, _, other) = await SeedAsync();
        var byOther = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("otro"), OrgRole.Member, other.UserId);

        using (var ownerHttp = tenants.ClientFor(tenants.A))
        using (var same = await MemberManagementApi.ChangeRoleAsync(ownerHttp, other.UserId, OrgRole.Admin))
            Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);

        Assert.Null((await InvitationRowAsync(factory.Services, byOther.Id)).RevokedAt);
        using var http = factory.CreateClient();
        using var accepted = await AcceptAnonymousAsync(http, byOther.RawToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    // ── Canje: el emisor se comprueba al aceptar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_REV5_H5_EmisorSinMembresiaVigenteAlAceptar_AunSinRevocarLaInvitacion_Responde422SinConsumirNiCrearNada()
    {
        var (tenants, admin, _) = await SeedAsync();
        var invitation = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("anterior"), OrgRole.Admin, admin.UserId);
        var forExisting = await SeedInvitationForExistingUserAsync(tenants, admin);
        // Baja directa en la base: no pasa por RemoveMemberCommand, así que la invitación sigue sin RevokedAt.
        await RemoveMembershipAsync(factory.Services, tenants.A.OrganizationId, admin.UserId);
        Assert.Null((await InvitationRowAsync(factory.Services, invitation.Id)).RevokedAt);

        using var http = factory.CreateClient();
        await AssertTokenInvalidAsync(await AcceptAnonymousAsync(http, invitation.RawToken));
        await AssertTokenInvalidAsync(await AcceptWithBearerAsync(http, forExisting.AccessToken, forExisting.Invitation.RawToken));

        Assert.Null((await InvitationRowAsync(factory.Services, invitation.Id)).AcceptedAt);
        Assert.Null((await InvitationRowAsync(factory.Services, forExisting.Invitation.Id)).AcceptedAt);
        Assert.Null(await UserByEmailAsync(factory.Services, invitation.Email));
        Assert.Equal(0, await MembershipCountForEmailAsync(factory.Services, tenants.A.OrganizationId, forExisting.Invitation.Email));
    }

    [Fact]
    public async Task PLAN0018_REV5_H5_EmisorAdminOOwnerVigente_SigueEmitiendoInvitacionesCanjeables()
    {
        var (tenants, admin, _) = await SeedAsync();
        var byAdmin = await SeedInvitationAsync(factory.Services, tenants.A.OrganizationId, NewEmail("emisor-vigente"), OrgRole.Admin, admin.UserId);
        var forExisting = await SeedInvitationForExistingUserAsync(tenants, tenants.A.Owner);

        using var http = factory.CreateClient();
        using (var anonymous = await AcceptAnonymousAsync(http, byAdmin.RawToken))
            Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        using (var withSession = await AcceptWithBearerAsync(http, forExisting.AccessToken, forExisting.Invitation.RawToken))
            Assert.Equal(HttpStatusCode.OK, withSession.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record ExistingUserInvitation(SeededInvitation Invitation, string AccessToken);

    // A con owner y dos admins: el emisor afectado y otro que no se toca.
    private async Task<(TenantPair Tenants, User Admin, User Other)> SeedAsync()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).WithMember(OrgRole.Admin).BuildAsync();
        var admins = MemberManagementApi.Admins(tenants.A);
        return (tenants, admins[0], admins[1]);
    }

    // Invitación emitida por issuer para el owner de B (cuenta existente y verificada), con su JWT de B para aceptarla.
    private async Task<ExistingUserInvitation> SeedInvitationForExistingUserAsync(TenantPair tenants, User issuer)
    {
        var invitee = tenants.B.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invitee.UserId);
        var invitation = await SeedInvitationAsync(
            factory.Services, tenants.A.OrganizationId, invitee.Email, OrgRole.Member, issuer.UserId);
        return new ExistingUserInvitation(invitation, tenants.TokenFor(tenants.B));
    }
}

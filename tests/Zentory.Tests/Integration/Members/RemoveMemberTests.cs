using System.Net;
using System.Text.Json;
using Zentory.API.Middleware;
using Zentory.Domain.Constants;
using Zentory.Tests.Security.Idor;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.MemberManagementApi;

namespace Zentory.Tests.Integration.Members;

// PLAN-0018 T28 sobre el host real: DELETE /organization/members/{userId} (RF-46 b, RF-48, RF-51, RF-87 b; D-19).
// CA-T28-1 (parte de CA-54), CA-T28-3 (parte de CA-56), CA-T28-4 (parte de CA-60) y CA-T28-5 (parte de CA-53). La
// organización de los casos tiene owner, dos admins y un member.
public sealed class RemoveMemberTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    // Ruta del esquema de usuario sin exclusión: con la membresía borrada, el middleware responde MEMBERSHIP_REVOKED.
    private const string MemberOnlyEndpoint = "/api/v1/organization/profile";

    // ── CA-T28-1 (parte de CA-54): sobre uno mismo y sobre el owner ─────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA54_OwnerYAdmin_SeBorranASiMismos_Reciben403AccessDeniedSinBaja()
    {
        var tenants = await FullOrganizationAsync(factory);
        var admin = Admins(tenants.A)[0];

        using (var owner = tenants.ClientFor(tenants.A))
        using (var self = await RemoveAsync(owner, tenants.A.Owner.UserId))
            await ForbiddenAssert.IsAccessDeniedAsync(self);
        using (var http = tenants.ClientFor(tenants.A, admin))
        using (var self = await RemoveAsync(http, admin.UserId))
            await ForbiddenAssert.IsAccessDeniedAsync(self);

        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId))!.DeletedAt);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, admin.UserId))!.DeletedAt);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA54_AdminOMember_BorranAlOwner_Reciben403AccessDeniedSinBaja(string actorRole)
    {
        var tenants = await FullOrganizationAsync(factory);
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(actorRole));

        using var response = await RemoveAsync(http, tenants.A.Owner.UserId);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId))!.DeletedAt);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_RolGuardadoDelOwnerAlteradoAAdmin_AdminLoBorra_Recibe403PorOwnerId()
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, owner.UserId, OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, Admins(tenants.A)[0]);

        using var response = await RemoveAsync(http, owner.UserId);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, owner.UserId))!.DeletedAt);

        // La fila del owner se alteró a propósito como precondición; restaurada, el escenario no dejó otra membresía owner
        // ni duplicados.
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, owner.UserId, OrgRole.Owner);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_RolGuardadoDelOwnerAlteradoAMember_AdminLoBorra_Recibe403PorOwnerId()
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, owner.UserId, OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, Admins(tenants.A)[0]);

        using var response = await RemoveAsync(http, owner.UserId);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, owner.UserId))!.DeletedAt);

        // La fila del owner se alteró a propósito como precondición; restaurada, el escenario no dejó otra membresía owner
        // ni duplicados.
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, owner.UserId, OrgRole.Owner);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Theory]
    [MemberData(nameof(AlternativeGuidFormats), MemberType = typeof(MemberManagementApi))]
    public async Task PLAN0018_CA54_GuidDelOwnerEnOtroFormato_AdminLoBorra_NoLoModifica(string format)
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        using var http = tenants.ClientFor(tenants.A, Admins(tenants.A)[0]);

        using var response = await RemoveAsync(http, FormatGuid(owner.UserId, format));

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"{format}: {(int)response.StatusCode}");
        var membership = await MembershipAsync(factory.Services, tenants.A.OrganizationId, owner.UserId);
        Assert.Null(membership!.DeletedAt);
        Assert.Equal(OrgRole.Owner, membership.Role);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_AdminDaDeBajaAOtroAdmin_Responde204ConBorradoLogico()
    {
        var tenants = await FullOrganizationAsync(factory);
        var (actor, target) = (Admins(tenants.A)[0], Admins(tenants.A)[1]);
        using var http = tenants.ClientFor(tenants.A, actor);

        using var response = await RemoveAsync(http, target.UserId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var membership = await MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId);
        Assert.NotNull(membership);
        Assert.NotNull(membership.DeletedAt);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── CA-T28-3 (parte de CA-56): efecto de la baja ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA56_MemberDadoDeBaja_Delete204_Access401MembershipRevoked_Refresh401_YaNoEstaEnLaLista(
        string actorRole)
    {
        var tenants = await FullOrganizationAsync(factory);
        var member = tenants.A.Member(OrgRole.Member);
        using var memberHttp = tenants.ClientFor(tenants.A, member);
        var refresh = await AuthSessionApi.SeedRefreshTokenAsync(factory.Services, member);

        // Control: antes de la baja, su token pasa.
        using (var before = await memberHttp.GetAsync(MemberOnlyEndpoint))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var actorHttp = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));
        using (var removed = await RemoveAsync(actorHttp, member.UserId))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        using (var after = await memberHttp.GetAsync(MemberOnlyEndpoint))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
            using var body = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
            Assert.Equal(MembershipValidationMiddleware.MembershipRevokedCode, body.RootElement.GetProperty("code").GetString());
        }

        // La baja revoca sus refresh tokens por sí misma, antes de que el afectado intente usarlos: RefreshTokenCommand
        // también los revoca al no encontrar membresía, así que mirar solo la respuesta del refresh no lo probaría.
        Assert.True((await AuthSessionApi.RefreshTokenRowAsync(factory.Services, refresh)).IsRevoked);
        Assert.All(await AuthSessionApi.RefreshTokensOfAsync(factory.Services, member.UserId), rt => Assert.True(rt.IsRevoked));

        using (var anonymous = factory.CreateClient())
        using (var refreshed = await AuthSessionApi.RefreshAsync(anonymous, refresh))
            await AuthSessionApi.AssertRefreshTokenInvalidAsync(refreshed);

        using var list = await actorHttp.GetAsync(MembersEndpoint);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var members = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.DoesNotContain(members.RootElement.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == member.UserId);
        Assert.Contains(members.RootElement.EnumerateArray(), m => m.GetProperty("userId").GetGuid() == tenants.A.Owner.UserId);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    [IdorCase("DELETE", RemoveRoute)]
    public async Task PLAN0018_CA56_AdminDeA_DeleteConUserIdDeMiembroDeB_Responde404SinCambiarLaMembresiaDeB()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Admin))
            .ForB(b => b.WithPlan(Plan.Pro).WithMember(OrgRole.Member))
            .BuildAsync();
        var memberOfB = tenants.B.Member(OrgRole.Member);
        var refreshOfB = await AuthSessionApi.SeedRefreshTokenAsync(factory.Services, memberOfB);
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));

        foreach (var target in new[] { memberOfB.UserId, tenants.B.Owner.UserId })
        {
            using var response = await RemoveAsync(http, target);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var membership = await MembershipAsync(factory.Services, tenants.B.OrganizationId, memberOfB.UserId);
        Assert.Null(membership!.DeletedAt);
        Assert.Equal(OrgRole.Member, membership.Role);
        Assert.False((await AuthSessionApi.RefreshTokenRowAsync(factory.Services, refreshOfB)).IsRevoked);
        Assert.Empty(await MemberLogsAsync(factory.Services, tenants.B.OrganizationId));

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA56_DeleteConUserIdInexistenteYConMiembroDeB_MismaRespuesta404SinDatosDeB()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Admin))
            .ForB(b => b.WithPlan(Plan.Pro).WithMember(OrgRole.Member))
            .BuildAsync();
        var memberOfB = tenants.B.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));

        using var unknown = await RemoveAsync(http, Guid.NewGuid());
        using var ofB = await RemoveAsync(http, memberOfB.UserId);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(unknown.StatusCode, ofB.StatusCode);
        var bodyOfB = await ofB.Content.ReadAsStringAsync();
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), bodyOfB);
        Assert.DoesNotContain(tenants.B.OrganizationId.ToString(), bodyOfB, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(memberOfB.UserId.ToString(), bodyOfB, StringComparison.OrdinalIgnoreCase);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA56_SegundaBajaDelMismoMiembro_DeleteYPatchRole_Responden404SinOtraFilaDeLog()
    {
        var tenants = await FullOrganizationAsync(factory);
        var target = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A);

        using (var first = await RemoveAsync(http, target.UserId))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using (var second = await RemoveAsync(http, target.UserId))
            Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        using (var change = await ChangeRoleAsync(http, target.UserId, OrgRole.Admin))
            Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);

        Assert.Single(await MemberLogsAsync(factory.Services, tenants.A.OrganizationId));

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA56_AdminIntentaBorrarAlOwnerYASiMismo_Recibe403_YLosRefreshDelOwnerSiguenSirviendo()
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        var admin = Admins(tenants.A)[0];
        var ownerRefresh = await AuthSessionApi.SeedRefreshTokenAsync(factory.Services, owner);
        using var http = tenants.ClientFor(tenants.A, admin);

        using (var onOwner = await RemoveAsync(http, owner.UserId))
            await ForbiddenAssert.IsAccessDeniedAsync(onOwner);
        using (var onSelf = await RemoveAsync(http, admin.UserId))
            await ForbiddenAssert.IsAccessDeniedAsync(onSelf);

        using var anonymous = factory.CreateClient();
        using var refreshed = await AuthSessionApi.RefreshAsync(anonymous, ownerRefresh);
        var session = await AuthSessionApi.ReadSessionAsync(refreshed);
        Assert.Equal(tenants.A.OrganizationId.ToString(), session.ActiveOrgId);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, admin.UserId))!.DeletedAt);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── CA-T28-4 (parte de CA-60): ActivityLog ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA60_Baja_DejaUnaFilaEnActivityLogConActorObjetivoAntesYDespues()
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        var target = Admins(tenants.A)[1];
        using var http = tenants.ClientFor(tenants.A);

        using (var response = await RemoveAsync(http, target.UserId))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var entry = Assert.Single(await MemberLogsAsync(factory.Services, tenants.A.OrganizationId));
        var membership = await MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId);
        Assert.Equal(membership!.Id, entry.EntityId);
        Assert.Equal(owner.UserId, entry.UserId);
        Assert.Equal(owner.UserId, entry.ActorUserId);
        Assert.Equal(target.UserId, entry.TargetUserId);
        Assert.Equal(OrgRole.Admin, entry.Before);
        Assert.Null(entry.After);
    }

    // ── CA-T28-5 (parte de CA-53): ManageMembers ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner, Plan.Pro)]
    [InlineData(OrgRole.Owner, Plan.Studio)]
    [InlineData(OrgRole.Admin, Plan.Pro)]
    [InlineData(OrgRole.Admin, Plan.Studio)]
    public async Task PLAN0018_CA53_OwnerOAdmin_EmpresaProOStudio_Delete_Responde204(string actorRole, string plan)
    {
        var tenants = await FullOrganizationAsync(factory, LegalType.Empresa, plan);
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await RemoveAsync(http, tenants.A.Member(OrgRole.Member).UserId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(OrgRole.Owner, Plan.Free)]
    [InlineData(OrgRole.Owner, Plan.Pro)]
    [InlineData(OrgRole.Owner, Plan.Studio)]
    [InlineData(OrgRole.Admin, Plan.Free)]
    [InlineData(OrgRole.Admin, Plan.Pro)]
    [InlineData(OrgRole.Admin, Plan.Studio)]
    public async Task PLAN0018_CA53_OwnerOAdmin_Freelance_Delete_Responde403AccessDeniedSinBaja(string actorRole, string plan)
    {
        var tenants = await FullOrganizationAsync(factory, LegalType.Freelance, plan);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await RemoveAsync(http, member.UserId);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, member.UserId))!.DeletedAt);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA53_OwnerOAdmin_EmpresaFree_Delete_Responde403PlanRequiredPro(string actorRole)
    {
        var tenants = await FullOrganizationAsync(factory, LegalType.Empresa, Plan.Free);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await RemoveAsync(http, member.UserId);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
        Assert.Null((await MembershipAsync(factory.Services, tenants.A.OrganizationId, member.UserId))!.DeletedAt);
    }
}

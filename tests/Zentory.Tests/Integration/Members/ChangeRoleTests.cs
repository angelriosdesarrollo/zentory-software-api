using System.Net;
using System.Text.Json;
using Zentory.Domain.Constants;
using Zentory.Tests.Security.Idor;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.MemberManagementApi;

namespace Zentory.Tests.Integration.Members;

// PLAN-0018 T28 sobre el host real: PATCH /organization/members/{userId}/role (RF-46 a, RF-48, RF-51, RF-87 b; D-19).
// CA-T28-1 (parte de CA-54), CA-T28-2 (parte de CA-55), CA-T28-3 en lo que toca al cambio de rol (IDOR, parte de CA-56),
// CA-T28-4 (parte de CA-60) y CA-T28-5 (parte de CA-53). La organización de los casos tiene owner, dos admins y un member.
public sealed class ChangeRoleTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string MemberPassword = "Miembro-Cambio-De-Rol-2026!";

    // ── CA-T28-1 (parte de CA-54): valores de roleId ────────────────────────────────────────────────────────────

    public static TheoryData<string?> NonAssignableRoles => new() { OrgRole.Owner, "superadmin", "", null };

    [Theory]
    [MemberData(nameof(NonAssignableRoles))]
    public async Task PLAN0018_CA54_OwnerAsignaRolNoAsignable_PatchRole_Responde422SinCambiarElRol(string? roleId)
    {
        var tenants = await FullOrganizationAsync(factory);
        var member = tenants.A.Member(OrgRole.Member);
        using var owner = tenants.ClientFor(tenants.A);

        using var response = await ChangeRoleAsync(owner, member.UserId, roleId);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("roleId", Assert.Single(body.RootElement.GetProperty("details").EnumerateArray())
            .GetProperty("field").GetString());
        Assert.Equal(OrgRole.Member, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, member.UserId))!.Role);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_AdminAsignaRolConOtraCapitalizacionOEspacios_PatchRole_Responde422SinCambiarElRol()
    {
        var tenants = await FullOrganizationAsync(factory);
        var target = Admins(tenants.A)[1];
        using var http = tenants.ClientFor(tenants.A, Admins(tenants.A)[0]);

        // La comparación es ordinal: "OWNER" o " owner" no son owner, pero tampoco se aceptan como otro rol.
        foreach (var roleId in new[]
                 { OrgRole.Owner.ToUpperInvariant(), " " + OrgRole.Owner, OrgRole.Admin.ToUpperInvariant() })
        {
            using var response = await ChangeRoleAsync(http, target.UserId, roleId);
            Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"'{roleId}': {(int)response.StatusCode}");
        }

        Assert.Equal(OrgRole.Admin, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId))!.Role);
        Assert.Empty(await MemberLogsAsync(factory.Services, tenants.A.OrganizationId));

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── CA-T28-1: sobre uno mismo y sobre el owner ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA54_OwnerYAdmin_CambianSuPropioRol_Reciben403AccessDenied()
    {
        var tenants = await FullOrganizationAsync(factory);
        var admin = Admins(tenants.A)[0];

        using (var owner = tenants.ClientFor(tenants.A))
        using (var self = await ChangeRoleAsync(owner, tenants.A.Owner.UserId, OrgRole.Admin))
            await ForbiddenAssert.IsAccessDeniedAsync(self);
        using (var http = tenants.ClientFor(tenants.A, admin))
        using (var self = await ChangeRoleAsync(http, admin.UserId, OrgRole.Member))
            await ForbiddenAssert.IsAccessDeniedAsync(self);

        Assert.Equal(OrgRole.Owner, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId))!.Role);
        Assert.Equal(OrgRole.Admin, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, admin.UserId))!.Role);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA54_AdminOMember_CambianElRolDelOwner_Reciben403AccessDenied(string actorRole)
    {
        var tenants = await FullOrganizationAsync(factory);
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(actorRole));

        using var response = await ChangeRoleAsync(http, tenants.A.Owner.UserId, OrgRole.Member);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(OrgRole.Owner, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId))!.Role);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_RolGuardadoDelOwnerAlteradoAAdmin_AdminCambiaSuRol_Recibe403PorOwnerId()
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        // La protección es por Organization.OwnerId (RF-87 b): con la fila del owner en admin, un admin sigue sin poder.
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, owner.UserId, OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, Admins(tenants.A)[0]);

        using var response = await ChangeRoleAsync(http, owner.UserId, OrgRole.Member);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(OrgRole.Admin, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, owner.UserId))!.Role);

        // La fila del owner se alteró a propósito como precondición; restaurada, el escenario no dejó otra membresía owner
        // ni duplicados.
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, owner.UserId, OrgRole.Owner);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Theory]
    [MemberData(nameof(AlternativeGuidFormats), MemberType = typeof(MemberManagementApi))]
    public async Task PLAN0018_CA54_GuidDelOwnerEnOtroFormato_AdminCambiaSuRol_NoLoModifica(string format)
    {
        var tenants = await FullOrganizationAsync(factory);
        var owner = tenants.A.Owner;
        using var http = tenants.ClientFor(tenants.A, Admins(tenants.A)[0]);

        using var response = await ChangeRoleAsync(http, FormatGuid(owner.UserId, format), OrgRole.Member);

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"{format}: {(int)response.StatusCode}");
        Assert.Equal(OrgRole.Owner, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, owner.UserId))!.Role);
        Assert.Empty(await MemberLogsAsync(factory.Services, tenants.A.OrganizationId));

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_AdminCambiaAOtroAdminAMember_Responde204YElRolCambia()
    {
        var tenants = await FullOrganizationAsync(factory);
        var (actor, target) = (Admins(tenants.A)[0], Admins(tenants.A)[1]);
        using var http = tenants.ClientFor(tenants.A, actor);

        using var response = await ChangeRoleAsync(http, target.UserId, OrgRole.Member);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(OrgRole.Member, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId))!.Role);

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── CA-T28-2 (parte de CA-55): el rol nuevo rige en la siguiente petición ───────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA55_MemberPromovidoAAdmin_AccessAnteriorTieneRolAdmin_RefreshAnterior401_LoginNuevoMuestraAdmin(
        string actorRole)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro).WithMemberPassword(MemberPassword)
                .WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);
        var actor = actorRole == OrgRole.Owner ? tenants.A.Owner : tenants.A.Member(OrgRole.Admin);
        var previousAccess = tenants.TokenFor(tenants.A, member);
        var previousRefresh = await AuthSessionApi.SeedRefreshTokenAsync(factory.Services, member);
        using var memberHttp = AuthSessionApi.Bearer(factory, previousAccess);

        // Control: como member, la escritura de admin se niega.
        using (var denied = await PatchSettingsAsync(memberHttp))
            await ForbiddenAssert.IsAccessDeniedAsync(denied);

        using (var actorHttp = tenants.ClientFor(tenants.A, actor))
        using (var changed = await ChangeRoleAsync(actorHttp, member.UserId, OrgRole.Admin))
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using (var allowed = await PatchSettingsAsync(memberHttp))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var anonymous = factory.CreateClient();
        using (var refreshed = await AuthSessionApi.RefreshAsync(anonymous, previousRefresh))
            await AuthSessionApi.AssertRefreshTokenInvalidAsync(refreshed);

        using var login = await AuthSessionApi.LoginAsync(anonymous, member.Email, MemberPassword);
        var session = await AuthSessionApi.ReadSessionAsync(login);
        Assert.Equal(OrgRole.Admin, session.Claim("active_org_role"));
        Assert.Equal(tenants.A.OrganizationId.ToString(), session.Claim("active_org_id"));

        using var fresh = AuthSessionApi.Bearer(factory, session.RawAccessToken);
        using var organizations = await fresh.GetAsync(AuthSessionApi.MyOrgsEndpoint);
        Assert.Equal(HttpStatusCode.OK, organizations.StatusCode);
        using var body = JsonDocument.Parse(await organizations.Content.ReadAsStringAsync());
        var ofA = Assert.Single(body.RootElement.EnumerateArray(),
            o => o.GetProperty("orgId").GetString() == tenants.A.OrganizationId.ToString());
        Assert.Equal(OrgRole.Admin, ofA.GetProperty("role").GetString());
    }

    // ── CA-T28-3 (parte de CA-56): userId de otra organización ──────────────────────────────────────────────────

    [Fact]
    [IdorCase("PATCH", ChangeRoleRoute)]
    public async Task PLAN0018_CA56_AdminDeA_PatchRoleConUserIdDeMiembroDeB_Responde404SinCambiarLaMembresiaDeB()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Admin))
            .ForB(b => b.WithPlan(Plan.Pro).WithMember(OrgRole.Member))
            .BuildAsync();
        var memberOfB = tenants.B.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Admin));

        foreach (var target in new[] { memberOfB.UserId, tenants.B.Owner.UserId })
        {
            using var response = await ChangeRoleAsync(http, target, OrgRole.Admin);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(OrgRole.Member, (await MembershipAsync(factory.Services, tenants.B.OrganizationId, memberOfB.UserId))!.Role);
        Assert.Equal(OrgRole.Owner, (await MembershipAsync(factory.Services, tenants.B.OrganizationId, tenants.B.Owner.UserId))!.Role);
        Assert.Empty(await MemberLogsAsync(factory.Services, tenants.B.OrganizationId));
        Assert.Empty(await MemberLogsAsync(factory.Services, tenants.A.OrganizationId));

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── CA-T28-4 (parte de CA-60): ActivityLog ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA60_CambioDeRol_DejaUnaFilaEnActivityLogConActorObjetivoAntesYDespues()
    {
        var tenants = await FullOrganizationAsync(factory);
        var actor = Admins(tenants.A)[0];
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, actor);

        using (var response = await ChangeRoleAsync(http, member.UserId, OrgRole.Admin))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var entry = Assert.Single(await MemberLogsAsync(factory.Services, tenants.A.OrganizationId));
        var membership = await MembershipAsync(factory.Services, tenants.A.OrganizationId, member.UserId);
        Assert.Equal(membership!.Id, entry.EntityId);
        Assert.Equal(actor.UserId, entry.UserId);
        Assert.Equal(actor.UserId, entry.ActorUserId);
        Assert.Equal(member.UserId, entry.TargetUserId);
        Assert.Equal(OrgRole.Member, entry.Before);
        Assert.Equal(OrgRole.Admin, entry.After);
    }

    // ── CA-T28-5 (parte de CA-53): ManageMembers ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner, Plan.Pro)]
    [InlineData(OrgRole.Owner, Plan.Studio)]
    [InlineData(OrgRole.Admin, Plan.Pro)]
    [InlineData(OrgRole.Admin, Plan.Studio)]
    public async Task PLAN0018_CA53_OwnerOAdmin_EmpresaProOStudio_PatchRole_Responde204(string actorRole, string plan)
    {
        var tenants = await FullOrganizationAsync(factory, LegalType.Empresa, plan);
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await ChangeRoleAsync(http, tenants.A.Member(OrgRole.Member).UserId, OrgRole.Admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(OrgRole.Owner, Plan.Free)]
    [InlineData(OrgRole.Owner, Plan.Pro)]
    [InlineData(OrgRole.Owner, Plan.Studio)]
    [InlineData(OrgRole.Admin, Plan.Free)]
    [InlineData(OrgRole.Admin, Plan.Pro)]
    [InlineData(OrgRole.Admin, Plan.Studio)]
    public async Task PLAN0018_CA53_OwnerOAdmin_Freelance_PatchRole_Responde403AccessDeniedSinCambios(
        string actorRole, string plan)
    {
        var tenants = await FullOrganizationAsync(factory, LegalType.Freelance, plan);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await ChangeRoleAsync(http, member.UserId, OrgRole.Admin);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(OrgRole.Member, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, member.UserId))!.Role);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA53_OwnerOAdmin_EmpresaFree_PatchRole_Responde403PlanRequiredPro(string actorRole)
    {
        var tenants = await FullOrganizationAsync(factory, LegalType.Empresa, Plan.Free);
        var member = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, Actor(tenants.A, actorRole));

        using var response = await ChangeRoleAsync(http, member.UserId, OrgRole.Admin);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
        Assert.Equal(OrgRole.Member, (await MembershipAsync(factory.Services, tenants.A.OrganizationId, member.UserId))!.Role);
    }
}

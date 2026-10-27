using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Security.Idor;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Members;

// PLAN-0018 CA-52 (SPEC-0001 CA-25.1; SR-4.1; decisión del humano 1; rev. 4) sobre el host real: la matriz owner/admin/member
// × endpoint, una celda por caso (42), en una organización empresa + pro con owner, admin, member y un member objetivo.
//   - Administración (8 endpoints): owner y admin 2xx; member 403 ACCESS_DENIED sin efecto.
//   - Solo owner (5 endpoints): owner 2xx; admin y member 403 ACCESS_DENIED sin efecto (la organización sigue sin
//     DeletedAt y no se crea ninguna propuesta).
//   - Cancelar la transferencia: owner 204; admin y member 403 ACCESS_DENIED sin cancelar la pendiente.
// Tras cada celda se comprueba la invariante de membresías (CA-54; RF-48 e).
public sealed class RoleMatrixTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword  = "Owner-Matriz-2026!";
    private const string MemberPassword = "Miembro-Matriz-2026!";

    private const string ProfileEndpoint     = "/api/v1/organization/profile";
    private const string RolesEndpoint       = "/api/v1/organization/roles";
    private const string PermissionsEndpoint = "/api/v1/organization/roles/permissions";
    private const string PlanEndpoint        = "/api/v1/organization/plan";
    private const string BillingEndpoint     = "/api/v1/organization/billing-history";
    private const string InviteEndpoint      = MemberManagementApi.MembersEndpoint + "/invite";

    // Celdas de la matriz por grupo, en el orden de CA-52.
    public static readonly string[] AdministrationEndpoints =
    [
        "PATCH /organization/profile",
        "PATCH /organization/settings",
        "GET /organization/members",
        "PATCH /organization/members/{id}/role",
        "DELETE /organization/members/{id}",
        "POST /organization/members/invite",
        "GET /organization/roles",
        "GET /organization/roles/permissions",
    ];

    public static readonly string[] OwnerOnlyEndpoints =
    [
        "POST /organization/transfer-ownership",
        "GET /organization/transfer-ownership/preview",
        "GET /organization/plan",
        "GET /organization/billing-history",
        "DELETE /organization",
    ];

    public static readonly string[] CancelEndpoints = ["DELETE /organization/transfer-ownership"];

    private static readonly string[] Roles = [OrgRole.Owner, OrgRole.Admin, OrgRole.Member];

    public static TheoryData<string, string> AdministrationCells => Cells(AdministrationEndpoints);
    public static TheoryData<string, string> OwnerOnlyCells      => Cells(OwnerOnlyEndpoints);
    public static TheoryData<string, string> CancelCells         => Cells(CancelEndpoints);

    [Fact]
    public void PLAN0018_CA52_Matriz_TieneCuarentaYDosCeldas_TresRolesPorCatorceEndpoints()
    {
        Assert.Equal(42, AdministrationCells.Count() + OwnerOnlyCells.Count() + CancelCells.Count());
        Assert.Equal(14, AdministrationEndpoints.Concat(OwnerOnlyEndpoints).Concat(CancelEndpoints).Distinct().Count());
    }

    // ── Administración: owner y admin 2xx, member 403 sin efecto ────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AdministrationCells))]
    public async Task PLAN0018_CA52_Matriz_Administracion_OwnerYAdmin2xx_Member403AccessDeniedSinEfecto(string endpoint, string role)
    {
        var (tenants, http, target) = await ArrangeAsync(role);
        var allowed = role != OrgRole.Member;

        switch (endpoint)
        {
            case "PATCH /organization/profile":
            {
                var legalName = $"Razón social {role} {Guid.NewGuid():N}";
                using var response = await http.PatchAsJsonAsync(ProfileEndpoint, new { legalName });
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                Assert.Equal(allowed, await SettingAsync(tenants.A.OrganizationId, OrgSettingKey.ProfileLegalName) == legalName);
                break;
            }
            case "PATCH /organization/settings":
            {
                using var response = await MemberManagementApi.PatchSettingsAsync(http);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                Assert.Equal(allowed, await SettingAsync(tenants.A.OrganizationId, OrgSettingKey.ProposalValidity) is not null);
                break;
            }
            case "GET /organization/members":
            {
                using var response = await http.GetAsync(MemberManagementApi.MembersEndpoint);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                break;
            }
            case "PATCH /organization/members/{id}/role":
            {
                using var response = await MemberManagementApi.ChangeRoleAsync(http, target.UserId, OrgRole.Admin);
                await AssertCellAsync(response, allowed, HttpStatusCode.NoContent);
                var membership = await MemberManagementApi.MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId);
                Assert.Equal(allowed ? OrgRole.Admin : OrgRole.Member, membership!.Role);
                break;
            }
            case "DELETE /organization/members/{id}":
            {
                using var response = await MemberManagementApi.RemoveAsync(http, target.UserId);
                await AssertCellAsync(response, allowed, HttpStatusCode.NoContent);
                var membership = await MemberManagementApi.MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId);
                Assert.Equal(allowed, membership!.DeletedAt is not null);
                break;
            }
            case "POST /organization/members/invite":
            {
                var email = $"invitado-matriz-{Guid.NewGuid():N}@zentory.test";
                using var response = await http.PostAsJsonAsync(InviteEndpoint, new { email, roleId = OrgRole.Member });
                await AssertCellAsync(response, allowed, HttpStatusCode.Accepted);
                Assert.Equal(allowed ? 1 : 0, await InvitationCountAsync(tenants.A.OrganizationId));
                break;
            }
            case "GET /organization/roles":
            {
                using var response = await http.GetAsync(RolesEndpoint);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                break;
            }
            case "GET /organization/roles/permissions":
            {
                using var response = await http.GetAsync(PermissionsEndpoint);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Celda sin caso.");
        }

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── Solo owner: owner 2xx, admin y member 403 sin efecto ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OwnerOnlyCells))]
    public async Task PLAN0018_CA52_Matriz_SoloOwner_Owner2xx_AdminYMember403AccessDeniedSinEfecto(string endpoint, string role)
    {
        var (tenants, http, target) = await ArrangeAsync(role);
        var allowed = role == OrgRole.Owner;
        var password = allowed ? OwnerPassword : MemberPassword;
        var ownerBefore = await OwnerIdAsync(tenants.A.OrganizationId);

        switch (endpoint)
        {
            case "POST /organization/transfer-ownership":
            {
                // El owner acusa el aviso vigente; admin y member no pueden pedirlo, así que mandan un acuse cualquiera:
                // la policy responde antes de mirar el cuerpo.
                var hash = allowed ? await OwnershipTransferApi.PreviewHashAsync(http, target.UserId) : new string('0', 64);
                using var response = await OwnershipTransferApi.ProposeAsync(http, target.UserId, hash, password);
                await AssertCellAsync(response, allowed, HttpStatusCode.Accepted);
                // Proponer no cambia nada (rev. 4): el owner y los roles siguen igual; solo existe la propuesta del owner.
                Assert.Equal(allowed ? 1 : 0,
                    (await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId)).Count);
                break;
            }
            case "GET /organization/transfer-ownership/preview":
            {
                using var response = await OwnershipTransferApi.PreviewAsync(http, target.UserId);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                break;
            }
            case "GET /organization/plan":
            {
                using var response = await http.GetAsync(PlanEndpoint);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                break;
            }
            case "GET /organization/billing-history":
            {
                using var response = await http.GetAsync(BillingEndpoint);
                await AssertCellAsync(response, allowed, HttpStatusCode.OK);
                break;
            }
            case "DELETE /organization":
            {
                using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, password);
                await AssertCellAsync(response, allowed, HttpStatusCode.NoContent);
                var organization = await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId);
                Assert.Equal(allowed, organization.DeletedAt is not null);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Celda sin caso.");
        }

        if (!allowed)
        {
            Assert.Null((await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).DeletedAt);
            Assert.Empty(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));
        }
        Assert.Equal(ownerBefore, await OwnerIdAsync(tenants.A.OrganizationId));
        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── Cancelar la transferencia: owner 204, admin y member 403 sin cancelar ───────────────────────────────────

    [Theory]
    [MemberData(nameof(CancelCells))]
    public async Task PLAN0018_CA52_Matriz_CancelarTransferencia_Owner204_AdminYMember403AccessDeniedSinCancelar(string endpoint, string role)
    {
        Assert.Equal(CancelEndpoints[0], endpoint);
        var (tenants, http, target) = await ArrangeAsync(role);
        var allowed = role == OrgRole.Owner;
        var pending = await PendingTransferSeed.SeedAsync(factory.Services, tenants.A, target);

        using var response = await OwnershipTransferApi.CancelAsync(http);

        await AssertCellAsync(response, allowed, HttpStatusCode.NoContent);
        var transfer = await OwnershipTransferApi.TransferAsync(factory.Services, pending.Id);
        Assert.Equal(allowed ? OwnershipTransferStatus.Cancelled : OwnershipTransferStatus.Pending, transfer.Status);
        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── CA-54 (rev. 4): el admin no hace nada de lo del owner ───────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA54_Admin_NoProponeNiCancelaNiEliminaNiVePlanNiBillingHistory_403SinEfecto()
    {
        var (tenants, http, target) = await ArrangeAsync(OrgRole.Admin);
        var pending = await PendingTransferSeed.SeedAsync(factory.Services, tenants.A, target);

        using (var propose = await OwnershipTransferApi.ProposeAsync(http, target.UserId, new string('0', 64), MemberPassword))
            await ForbiddenAssert.IsAccessDeniedAsync(propose);
        using (var cancel = await OwnershipTransferApi.CancelAsync(http))
            await ForbiddenAssert.IsAccessDeniedAsync(cancel);
        using (var delete = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, MemberPassword))
            await ForbiddenAssert.IsAccessDeniedAsync(delete);
        using (var plan = await http.GetAsync(PlanEndpoint))
            await ForbiddenAssert.IsAccessDeniedAsync(plan);
        using (var billing = await http.GetAsync(BillingEndpoint))
            await ForbiddenAssert.IsAccessDeniedAsync(billing);

        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).DeletedAt);
        var transfer = Assert.Single(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Equal((pending.Id, OwnershipTransferStatus.Pending), (transfer.Id, transfer.Status));
        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static TheoryData<string, string> Cells(IEnumerable<string> endpoints)
    {
        var data = new TheoryData<string, string>();
        foreach (var endpoint in endpoints)
        foreach (var role in Roles)
            data.Add(endpoint, role);
        return data;
    }

    // Organización A empresa + pro con owner, admin, member (el que actúa como member) y un member objetivo, todos con
    // contraseña y email verificado; devuelve el cliente del rol pedido y el objetivo.
    private async Task<(TenantPair Tenants, HttpClient Http, User Target)> ArrangeAsync(string role)
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword,
            configureA: a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Member));
        var members = tenants.A.Members.Where(m => m.Role == OrgRole.Member).Select(m => m.User).ToList();
        Assert.Equal(2, members.Count);

        var actor = role switch
        {
            _ when role == OrgRole.Owner => tenants.A.Owner,
            _ when role == OrgRole.Admin => tenants.A.Member(OrgRole.Admin),
            _                            => members[0],
        };
        return (tenants, tenants.ClientFor(tenants.A, actor), members[1]);
    }

    private static async Task AssertCellAsync(HttpResponseMessage response, bool allowed, HttpStatusCode expected)
    {
        if (allowed)
            Assert.Equal(expected, response.StatusCode);
        else
            await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    private async Task<string?> SettingAsync(Guid organizationId, string key)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
    }

    private async Task<int> InvitationCountAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationInvitations.IgnoreQueryFilters().CountAsync(i => i.OrganizationId == organizationId);
    }

    private async Task<Guid?> OwnerIdAsync(Guid organizationId)
        => (await OrganizationDeletionApi.OrganizationAsync(factory.Services, organizationId)).OwnerId;

}

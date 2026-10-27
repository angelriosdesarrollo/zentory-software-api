using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Security.Idor;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Members;

// PLAN-0018 T27 sobre el host real (RF-45, RF-47; D-18, D-19; DEC-0014): catálogo fijo de roles, matriz de permisos y
// gating de la gestión de miembros con ManageMembers. CA-T27-1 (parte de CA-51), CA-T27-4 (parte de CA-53) y la parte de
// CA-95 que toca la matriz y restore. El plan sale de la suscripción del owner (TenantBuilder); los casos de plan
// emiten a propósito un claim plan distinto del efectivo, así que solo pasan si la policy lee EffectivePlan (D-26).
public sealed class RolesCatalogTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string MembersEndpoint     = "/api/v1/organization/members";
    private const string RolesEndpoint       = "/api/v1/organization/roles";
    private const string PermissionsEndpoint = "/api/v1/organization/roles/permissions";

    private static readonly string[] ManagedEndpoints = [MembersEndpoint, RolesEndpoint, PermissionsEndpoint];

    // Matriz de RF-45 b, escrita aparte del código de producción: owner, admin, member.
    private static readonly Dictionary<string, (bool Owner, bool Admin, bool Member)> ExpectedMatrix = new()
    {
        ["settings.manage"]             = (true, true,  false),
        ["organization.fiscal_profile"] = (true, true,  false),
        ["members.manage"]              = (true, true,  false),
        ["finances.view"]               = (true, true,  false),
        ["billing.plan"]                = (true, false, false),
        ["organization.transfer"]       = (true, false, false),
        ["organization.delete"]         = (true, false, false),
        ["operations.access"]           = (true, true,  true),
    };

    // ── CA-T27-1 (parte de CA-51): catálogo y matriz ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA51_OwnerEmpresaPro_GetRoles_DevuelveExactamenteOwnerAdminYMember()
    {
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithPlan(Plan.Pro)).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var body = await GetJsonAsync(http, RolesEndpoint);
        var roles = body.RootElement.EnumerateArray().ToList();

        Assert.Equal([OrgRole.Owner, OrgRole.Admin, OrgRole.Member], roles.Select(r => r.GetProperty("id").GetString()));
        Assert.Equal(["Propietario", "Administrador", "Miembro"], roles.Select(r => r.GetProperty("name").GetString()));
        Assert.All(roles, role => Assert.True(role.GetProperty("isDefault").GetBoolean()));
    }

    [Fact]
    public async Task PLAN0018_CA51_OwnerEmpresaPro_GetRolesPermissions_DevuelveLaMatrizDeRF45()
    {
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithPlan(Plan.Pro)).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        var actual = await MatrixAsync(http);

        Assert.Equal(ExpectedMatrix.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, grants) in ExpectedMatrix)
            Assert.True(grants == actual[key], $"{key}: se esperaba {grants} y llegó {actual[key]}.");
    }

    [Fact]
    public async Task PLAN0018_CA95_GetRolesPermissions_OrganizationDelete_SoloParaOwner()
    {
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithPlan(Plan.Studio)).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        var matrix = await MatrixAsync(http);

        Assert.Equal((true, false, false), matrix["organization.delete"]);
    }

    // ── CA-95: restore comprueba OwnerId == sub ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA95_RestoreDeLaOrganizacionEliminada_AdminRecibe404SinRestaurar_OwnerRestaura()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        var a = tenants.A;
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(factory.Services, a.Plan, a.LegalType, limit: null);
        await OrganizationRestoreApi.MarkDeletedAsync(factory.Services, a.OrganizationId, a.Owner.UserId,
            DateTime.UtcNow.AddDays(-1));

        using (var admin = ClientWithoutOrganization(a.Member(OrgRole.Admin)))
        using (var denied = await OrganizationRestoreApi.RestoreAsync(admin, a.OrganizationId))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(factory.Services, a.OrganizationId)).DeletedAt);

        using var owner = ClientWithoutOrganization(a.Owner);
        using var restored = await OrganizationRestoreApi.RestoreAsync(owner, a.OrganizationId);
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(factory.Services, a.OrganizationId)).DeletedAt);
    }

    // ── CA-T27-4 (parte de CA-53): ManageMembers ────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> OwnerAndAdmin => new()
    {
        { OrgRole.Owner, Plan.Pro },
        { OrgRole.Owner, Plan.Studio },
        { OrgRole.Admin, Plan.Pro },
        { OrgRole.Admin, Plan.Studio },
    };

    [Theory]
    [MemberData(nameof(OwnerAndAdmin))]
    public async Task PLAN0018_CA53_OwnerOAdmin_EmpresaProOStudio_LasTresRutas_Responden200(string role, string plan)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(plan).WithMember(OrgRole.Admin))
            .BuildAsync();
        using var http = ClientWithMisleadingPlanClaim(tenants.A, UserWithRole(tenants.A, role), role, plan);

        foreach (var path in ManagedEndpoints)
        {
            using var response = await http.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role}/{plan} {path}: {(int)response.StatusCode}");
        }
    }

    [Theory]
    [InlineData(OrgRole.Owner, Plan.Free)]
    [InlineData(OrgRole.Owner, Plan.Pro)]
    [InlineData(OrgRole.Owner, Plan.Studio)]
    [InlineData(OrgRole.Admin, Plan.Free)]
    [InlineData(OrgRole.Admin, Plan.Pro)]
    [InlineData(OrgRole.Admin, Plan.Studio)]
    public async Task PLAN0018_CA53_OwnerOAdmin_Freelance_CualquierPlan_LasTresRutas_Responden403AccessDenied(
        string role, string plan)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Freelance).WithPlan(plan).WithMember(OrgRole.Admin))
            .BuildAsync();
        using var http = ClientWithMisleadingPlanClaim(tenants.A, UserWithRole(tenants.A, role), role, plan);

        foreach (var path in ManagedEndpoints)
        {
            using var response = await http.GetAsync(path);
            await ForbiddenAssert.IsAccessDeniedAsync(response);
        }
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA53_OwnerOAdmin_EmpresaFree_LasTresRutas_Responden403PlanRequiredPro(string role)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Free).WithMember(OrgRole.Admin))
            .BuildAsync();
        // El claim dice studio: si la policy lo leyera, respondería 200.
        using var http = ClientWithMisleadingPlanClaim(tenants.A, UserWithRole(tenants.A, role), role, Plan.Free);

        foreach (var path in ManagedEndpoints)
        {
            using var response = await http.GetAsync(path);
            await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
        }
    }

    [Theory]
    [InlineData(Plan.Pro)]
    [InlineData(Plan.Studio)]
    public async Task PLAN0018_CA53_Member_EmpresaProOStudio_LasTresRutas_Responden403AccessDenied(string plan)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(plan).WithMember(OrgRole.Member))
            .BuildAsync();
        var member = tenants.A.Member(OrgRole.Member);

        // Con su token real y con uno cuyo claim active_org_role dice owner: el rol sale de la membresía, no del claim.
        foreach (var claimedRole in new[] { OrgRole.Member, OrgRole.Owner })
        {
            using var http = ClientWithToken(factory.Tokens.ForUser(member, tenants.A.Organization, claimedRole, plan));
            foreach (var path in ManagedEndpoints)
            {
                using var response = await http.GetAsync(path);
                await ForbiddenAssert.IsAccessDeniedAsync(response);
            }
        }
    }

    [Fact]
    public async Task PLAN0018_CA53_GetMembers_MismoDtoQueAntes_ConLosMiembrosDeLaOrganizacionActiva()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var body = await GetJsonAsync(http, MembersEndpoint);
        var members = body.RootElement.EnumerateArray().ToList();

        // Contrato que consume el frontend: { userId, firstName, lastName, email, role, joinedAt }.
        Assert.All(members, member => Assert.Equal(
            ["email", "firstName", "joinedAt", "lastName", "role", "userId"],
            member.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        Assert.Equal(
            new[] { OrgRole.Owner, OrgRole.Admin, OrgRole.Member }.Order(StringComparer.Ordinal),
            members.Select(m => m.GetProperty("role").GetString()!).Order(StringComparer.Ordinal));
        Assert.Contains(members, m => m.GetProperty("userId").GetGuid() == tenants.A.Owner.UserId);
    }

    // ── IDOR (RF-13): las rutas nuevas no muestran datos de otra organización ───────────────────────────────────

    [Fact]
    [IdorCase("GET", "api/v1/organization/members")]
    public async Task PLAN0018_CA53_IdorAssert_GetMembersConTokenDeA_NoIncluyeMiembrosDeB()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro).WithMember(OrgRole.Admin))
            .ForB(b => b.WithPlan(Plan.Pro).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);
        var memberOfB = tenants.B.Member(OrgRole.Member);

        var body = await IdorAssert.ListExcludesAsync(http, MembersEndpoint,
            [tenants.B.Owner.UserId, memberOfB.UserId], tenants.B.Owner.Email, memberOfB.Email);

        // Control positivo: el listado sí trae a los de A.
        Assert.Contains(tenants.A.Member(OrgRole.Admin).UserId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [IdorCase("GET", "api/v1/organization/roles")]
    [IdorCase("GET", "api/v1/organization/roles/permissions")]
    public async Task PLAN0018_CA51_IdorAssert_RolesYPermisos_SonElCatalogoFijo_IgualesParaAYB_SinDatosDeOrganizacion()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithPlan(Plan.Pro))
            .ForB(b => b.WithPlan(Plan.Studio))
            .BuildAsync();
        using var httpA = tenants.ClientFor(tenants.A);
        using var httpB = tenants.ClientFor(tenants.B);

        foreach (var path in new[] { RolesEndpoint, PermissionsEndpoint })
        {
            var ofA = await IdorAssert.ListExcludesAsync(httpA, path,
                [tenants.B.OrganizationId, tenants.B.Owner.UserId, tenants.A.OrganizationId], tenants.B.Organization.Name);
            Assert.Equal(ofA, await IdorAssert.GetOkAsync(httpB, path));
        }
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static User UserWithRole(SeededTenant tenant, string role)
        => role == OrgRole.Owner ? tenant.Owner : tenant.Member(role);

    // Token con el rol real y un claim plan distinto del efectivo: studio para free y pro, free para studio.
    private HttpClient ClientWithMisleadingPlanClaim(SeededTenant tenant, User user, string role, string effectivePlan)
    {
        var claim = effectivePlan == Plan.Studio ? Plan.Free : Plan.Studio;
        return ClientWithToken(factory.Tokens.ForUser(user, tenant.Organization, role, claim));
    }

    private HttpClient ClientWithoutOrganization(User user)
        => ClientWithToken(OrganizationRestoreApi.TokenWithoutOrganization(factory.Services, user));

    private HttpClient ClientWithToken(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<Dictionary<string, (bool Owner, bool Admin, bool Member)>> MatrixAsync(HttpClient http)
    {
        using var body = await GetJsonAsync(http, PermissionsEndpoint);
        return body.RootElement.EnumerateArray().ToDictionary(
            row => row.GetProperty("key").GetString()!,
            row =>
            {
                Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("label").GetString()));
                var grants = row.GetProperty("grants");
                return (grants.GetProperty(OrgRole.Owner).GetBoolean(),
                        grants.GetProperty(OrgRole.Admin).GetBoolean(),
                        grants.GetProperty(OrgRole.Member).GetBoolean());
            },
            StringComparer.Ordinal);
    }
}

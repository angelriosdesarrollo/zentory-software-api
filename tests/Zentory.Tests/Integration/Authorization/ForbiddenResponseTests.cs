using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Authorization;

// PLAN-0018 RF-58 (CA-73; D-8): los 403 de las policies llevan cuerpo con code, con precedencia tipo de cuenta → plan →
// rol, sobre el host real. Las policies sin endpoint real (RequiresPro, RequiresStudio, EmpresaPro) se prueban con las
// acciones de AuthorizationProbeApiFactory; EmpresaOnly, EmpresaStudio y RequireOrgAdmin, con endpoints reales.
public sealed class ForbiddenResponseTests(AuthorizationProbeApiFactory factory) : IClassFixture<AuthorizationProbeApiFactory>
{
    private const string CashFlowEndpoint      = "/api/v1/cash-flow-entries";      // EmpresaOnly
    private const string CollaboratorsEndpoint = "/api/v1/collaborators";          // EmpresaStudio
    private const string ProfileEndpoint       = "/api/v1/organization/profile";   // PATCH: RequireOrgAdmin

    // ── Tipo de cuenta ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA73_FreelanceEnEndpointEmpresaOnly_Devuelve403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Freelance).WithPlan(Plan.Studio))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.GetAsync(CashFlowEndpoint);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    [Fact]
    public async Task PLAN0018_CA73_FreelanceFreeEnEmpresaPro_Devuelve403AccessDenied_GanaElTipoDeCuenta()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Freelance).WithPlan(Plan.Free))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.EmpresaProPath);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    [Fact]
    public async Task PLAN0018_CA73_FreelanceFreeEnEmpresaStudioReal_Devuelve403AccessDenied_GanaElTipoDeCuenta()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Freelance).WithPlan(Plan.Free))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.GetAsync(CollaboratorsEndpoint);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    // ── Plan ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA73_EmpresaFreeEnRequiresPro_Devuelve403PlanRequiredPro()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Free))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.RequiresProPath);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
    }

    [Fact]
    public async Task PLAN0018_CA73_EmpresaFreeEnEmpresaPro_Devuelve403PlanRequiredPro()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Free))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.EmpresaProPath);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
    }

    [Theory]
    [InlineData(Plan.Free)]
    [InlineData(Plan.Pro)]
    public async Task PLAN0018_CA73_EmpresaSinStudioEnRequiresStudioYEmpresaStudio_Devuelve403PlanRequiredStudio(string plan)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(plan))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        using (var probe = await http.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath))
            await ForbiddenAssert.IsPlanRequiredAsync(probe, Plan.Studio);
        using (var real = await http.GetAsync(CollaboratorsEndpoint))
            await ForbiddenAssert.IsPlanRequiredAsync(real, Plan.Studio);
    }

    [Fact]
    public async Task PLAN0018_CA73_EmpresaConElPlanRequerido_PasaLasPoliciesDeTipoYPlan()
    {
        // Control positivo: el mismo pipeline deja pasar a quien cumple, sin cuerpo de 403.
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        foreach (var path in new[]
                 {
                     AuthorizationProbeApiFactory.RequiresProPath, AuthorizationProbeApiFactory.RequiresStudioPath,
                     AuthorizationProbeApiFactory.EmpresaProPath, CashFlowEndpoint, CollaboratorsEndpoint,
                 })
        {
            using var response = await http.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode}");
        }
    }

    // ── Rol ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA73_MemberEnRequireOrgAdmin_Devuelve403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Member).BuildAsync();
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));

        using (var probe = await http.GetAsync(AuthorizationProbeApiFactory.RequireOrgAdminPath))
            await ForbiddenAssert.IsAccessDeniedAsync(probe);
        using (var real = await http.PatchAsJsonAsync(ProfileEndpoint, new { legalName = "Intento del member" }))
            await ForbiddenAssert.IsAccessDeniedAsync(real);
    }

    // ── Precedencia con requisitos combinados (EmpresaPro + RequireOrgAdmin) ───────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA73_MemberEmpresaConPlanMenor_PlanYRolFallan_Devuelve403PlanRequired_GanaElPlan()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Free).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.EmpresaProOrgAdminPath);

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Pro);
    }

    [Fact]
    public async Task PLAN0018_CA73_MemberEmpresaConElPlanRequerido_SoloFallaElRol_Devuelve403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro).WithMember(OrgRole.Member))
            .BuildAsync();
        using var member = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));
        using var owner = tenants.ClientFor(tenants.A);

        using (var denied = await member.GetAsync(AuthorizationProbeApiFactory.EmpresaProOrgAdminPath))
            await ForbiddenAssert.IsAccessDeniedAsync(denied);

        // Control: el owner de la misma organización cumple los tres requisitos.
        using var ok = await owner.GetAsync(AuthorizationProbeApiFactory.EmpresaProOrgAdminPath);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA73_MemberFreelanceFree_TipoPlanYRolFallan_Devuelve403AccessDenied_GanaElTipoDeCuenta()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Freelance).WithPlan(Plan.Free).WithMember(OrgRole.Member))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.EmpresaProOrgAdminPath);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    // ── 401 sin cambios ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    public async Task PLAN0018_CA73_TokenAusenteOInvalido_Sigue401SinCuerpoDeForbidden(string? token)
    {
        using var http = factory.CreateClient();
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        foreach (var path in new[] { CashFlowEndpoint, AuthorizationProbeApiFactory.RequireOrgAdminPath, ProfileEndpoint })
        {
            using var response = await http.GetAsync(path);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }
    }
}

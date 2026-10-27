using System.Net;
using System.Net.Http.Headers;
using Zentory.API.Authorization;
using Zentory.API.Extensions;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Plans;

// PLAN-0018 T25 (SPEC-0001 CA-3, CA-13; TASK-QA-004; RF-58; RF-73; D-26) sobre el host real: los 6 estados legalType ×
// plan contra un endpoint de cada policy de tipo y plan, con el resultado de FEATURE_PERMISSIONS §4. El plan sale de la
// suscripción del owner (TenantBuilder); el token lleva a propósito un claim plan distinto del efectivo, así que la matriz
// solo pasa si las policies leen IMembershipContext.EffectivePlan.
//
// Endpoints reales: EmpresaOnly con el listado de transacciones (/cash-flow-entries) y EmpresaStudio con colaboradores
// y la vista de flujo de caja (/stats/cash-flow), DEC-0104; RequiresPro, RequiresStudio y EmpresaPro, sin endpoint real,
// con las acciones del probe.
public sealed class PolicyMatrixTests(AuthorizationProbeApiFactory factory) : IClassFixture<AuthorizationProbeApiFactory>
{
    private const string CollaboratorsEndpoint = "/api/v1/collaborators";      // EmpresaStudio
    private const string CashFlowEndpoint      = "/api/v1/cash-flow-entries";  // EmpresaOnly
    private const string CashFlowViewEndpoint  = "/api/v1/stats/cash-flow";    // EmpresaStudio (DEC-0104)

    // Resultado esperado: 2xx, o 403 con el código de RF-58 (y el plan requerido cuando es PLAN_REQUIRED).
    public sealed record Expected(string? Code, string? RequiredPlan = null)
    {
        public static readonly Expected Ok           = new((string?)null);
        public static readonly Expected AccessDenied = new(ZentoryAuthorizationResultHandler.AccessDeniedCode);
        public static Expected PlanRequired(string plan) => new(ZentoryAuthorizationResultHandler.PlanRequiredCode, plan);
    }

    private static readonly Dictionary<string, string[]> EndpointsByPolicy = new()
    {
        [PolicyNames.EmpresaOnly]    = [CashFlowEndpoint],
        [PolicyNames.RequiresPro]    = [AuthorizationProbeApiFactory.RequiresProPath],
        [PolicyNames.RequiresStudio] = [AuthorizationProbeApiFactory.RequiresStudioPath],
        [PolicyNames.EmpresaPro]     = [AuthorizationProbeApiFactory.EmpresaProPath],
        [PolicyNames.EmpresaStudio]  = [CollaboratorsEndpoint, CashFlowViewEndpoint],
    };

    // FEATURE_PERMISSIONS §4, por estado: EmpresaOnly, RequiresPro, RequiresStudio, EmpresaPro, EmpresaStudio. El tipo de
    // cuenta gana al plan (RF-58): un freelance studio recibe ACCESS_DENIED en las tres policies de empresa.
    public static TheoryData<string, string, Expected, Expected, Expected, Expected, Expected> Matrix => new()
    {
        { LegalType.Freelance, Plan.Free,   Expected.AccessDenied, Expected.PlanRequired(Plan.Pro), Expected.PlanRequired(Plan.Studio), Expected.AccessDenied,           Expected.AccessDenied },
        { LegalType.Freelance, Plan.Pro,    Expected.AccessDenied, Expected.Ok,                     Expected.PlanRequired(Plan.Studio), Expected.AccessDenied,           Expected.AccessDenied },
        { LegalType.Freelance, Plan.Studio, Expected.AccessDenied, Expected.Ok,                     Expected.Ok,                        Expected.AccessDenied,           Expected.AccessDenied },
        { LegalType.Empresa,   Plan.Free,   Expected.Ok,           Expected.PlanRequired(Plan.Pro), Expected.PlanRequired(Plan.Studio), Expected.PlanRequired(Plan.Pro), Expected.PlanRequired(Plan.Studio) },
        { LegalType.Empresa,   Plan.Pro,    Expected.Ok,           Expected.Ok,                     Expected.PlanRequired(Plan.Studio), Expected.Ok,                     Expected.PlanRequired(Plan.Studio) },
        { LegalType.Empresa,   Plan.Studio, Expected.Ok,           Expected.Ok,                     Expected.Ok,                        Expected.Ok,                     Expected.Ok },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task PLAN0018_CA47_EstadoLegalTypePorPlan_CadaPolicyDeTipoYPlan_Responde2xxO403SegunFeaturePermissions(
        string legalType, string plan,
        Expected empresaOnly, Expected requiresPro, Expected requiresStudio, Expected empresaPro, Expected empresaStudio)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(legalType).WithPlan(plan))
            .BuildAsync();
        using var http = ClientWithMisleadingPlanClaim(tenants, plan);

        var expected = new Dictionary<string, Expected>
        {
            [PolicyNames.EmpresaOnly]    = empresaOnly,
            [PolicyNames.RequiresPro]    = requiresPro,
            [PolicyNames.RequiresStudio] = requiresStudio,
            [PolicyNames.EmpresaPro]     = empresaPro,
            [PolicyNames.EmpresaStudio]  = empresaStudio,
        };

        foreach (var (policy, paths) in EndpointsByPolicy)
        foreach (var path in paths)
        {
            using var response = await http.GetAsync(path);
            await AssertOutcomeAsync(response, expected[policy], $"{legalType}/{plan} {policy} {path}");
        }
    }

    [Fact]
    public async Task PLAN0018_CA47_FreelanceStudio_PoliciesDeEmpresa_Siguen403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Freelance).WithPlan(Plan.Studio))
            .BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        foreach (var path in new[]
                 {
                     CashFlowEndpoint, AuthorizationProbeApiFactory.EmpresaProPath, CollaboratorsEndpoint,
                     CashFlowViewEndpoint,
                 })
        {
            using var response = await http.GetAsync(path);
            await ForbiddenAssert.IsAccessDeniedAsync(response);
        }

        // Control: el mismo token pasa las policies de plan que no exigen empresa.
        using var studio = await http.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath);
        Assert.Equal(HttpStatusCode.OK, studio.StatusCode);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Token del owner con un claim plan que no es el efectivo: studio para free y pro, free para studio.
    private HttpClient ClientWithMisleadingPlanClaim(TenantPair tenants, string effectivePlan)
    {
        var claim = effectivePlan == Plan.Studio ? Plan.Free : Plan.Studio;
        var token = factory.Tokens.ForUser(tenants.A.Owner, tenants.A.Organization, OrgRole.Owner, claim);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task AssertOutcomeAsync(HttpResponseMessage response, Expected expected, string label)
    {
        if (expected.Code is null)
        {
            Assert.True(response.IsSuccessStatusCode, $"{label}: {(int)response.StatusCode}");
            return;
        }

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{label}: {(int)response.StatusCode}");
        await ForbiddenAssert.HasCodeAsync(response, expected.Code, expected.RequiredPlan);
    }
}

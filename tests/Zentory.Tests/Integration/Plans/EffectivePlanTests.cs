using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.CashFlow.DTOs;
using Zentory.Application.Stats.Queries;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Domain.Entities.Common;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Plans;

// PLAN-0018 T25 (RF-40, RF-73, RF-75 a-c; ADR-0006 parte 3 anotada C2-f; D-26) sobre el host real: el plan que autoriza
// es el del owner de la organización activa, leído de su Subscription en cada petición por MembershipValidationMiddleware.
// El claim plan de los tokens no cuenta: los tokens de estos tests se emiten con un claim fijo y no se renuevan.
//
// CA-96: propagación sin re-login ni refresh a todas las organizaciones del owner y a sus members.
// CA-98: la bajada conserva las filas y bloquea en lectura la vista de flujo de caja (studio); el registro de
// transacciones sigue abierto a toda empresa (DEC-0104).
public sealed class EffectivePlanTests(AuthorizationProbeApiFactory factory) : IClassFixture<AuthorizationProbeApiFactory>
{
    private const string CashFlowEndpoint      = "/api/v1/cash-flow-entries";  // EmpresaOnly (DEC-0104)
    private const string CashFlowViewEndpoint  = "/api/v1/stats/cash-flow";    // EmpresaStudio (DEC-0104)
    private const string StatsFinanceEndpoint  = "/api/v1/stats/finance";      // indicadores básicos del mes
    private const string CollaboratorsEndpoint = "/api/v1/collaborators";      // EmpresaStudio

    // ── CA-96 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA96_SubidaDelOwnerAStudio_SinReloginNiRefresh_OwnerEnAYBYMemberEnAPasanRequiresStudio()
    {
        var scenario = await SeedOwnerWithTwoOrganizationsAndMemberAsync();

        // Antes: O es pro, y M (studio propio) recibe en A el plan de O.
        foreach (var (http, label) in scenario.ClientsInOwnerOrganizations())
        {
            using (var studio = await http.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath))
                await ForbiddenAssert.IsPlanRequiredAsync(studio, Plan.Studio);
            using var pro = await http.GetAsync(AuthorizationProbeApiFactory.RequiresProPath);
            Assert.True(pro.StatusCode == HttpStatusCode.OK, $"{label}: {(int)pro.StatusCode}");
        }

        await TenantBuilder.AssignPlanAsync(factory.Services, scenario.Owner, Plan.Studio);

        // Mismos tokens (claim plan = pro): la petición siguiente ya ve studio.
        foreach (var (http, label) in scenario.ClientsInOwnerOrganizations())
        {
            using (var probe = await http.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath))
                Assert.True(probe.StatusCode == HttpStatusCode.OK, $"{label} RequiresStudio: {(int)probe.StatusCode}");
            using var real = await http.GetAsync(CollaboratorsEndpoint);
            Assert.True(real.StatusCode == HttpStatusCode.OK, $"{label} EmpresaStudio: {(int)real.StatusCode}");
        }

        scenario.DisposeClients();
    }

    [Fact]
    public async Task PLAN0018_CA96_BajadaDelOwnerAFree_SinReloginNiRefresh_OwnerEnAYBYMemberEnARecibenPlanRequiredEnRequiresPro()
    {
        var scenario = await SeedOwnerWithTwoOrganizationsAndMemberAsync();
        await TenantBuilder.AssignPlanAsync(factory.Services, scenario.Owner, Plan.Studio);
        foreach (var (http, label) in scenario.ClientsInOwnerOrganizations())
        {
            using var before = await http.GetAsync(AuthorizationProbeApiFactory.RequiresProPath);
            Assert.True(before.StatusCode == HttpStatusCode.OK, $"{label}: {(int)before.StatusCode}");
        }

        await TenantBuilder.AssignPlanAsync(factory.Services, scenario.Owner, Plan.Free);

        foreach (var (http, _) in scenario.ClientsInOwnerOrganizations())
        {
            using (var pro = await http.GetAsync(AuthorizationProbeApiFactory.RequiresProPath))
                await ForbiddenAssert.IsPlanRequiredAsync(pro, Plan.Pro);
            using var studio = await http.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath);
            await ForbiddenAssert.IsPlanRequiredAsync(studio, Plan.Studio);
        }

        scenario.DisposeClients();
    }

    [Fact]
    public async Task PLAN0018_CA96_MemberConSuscripcionStudioPropia_RecibeEnAElPlanDelOwnerYEnSuOrganizacionElSuyo()
    {
        var scenario = await SeedOwnerWithTwoOrganizationsAndMemberAsync();

        foreach (var ownerPlan in new[] { Plan.Pro, Plan.Free, Plan.Studio, Plan.Pro })
        {
            await TenantBuilder.AssignPlanAsync(factory.Services, scenario.Owner, ownerPlan);

            using (var inA = await scenario.MemberInA.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath))
            {
                if (ownerPlan == Plan.Studio)
                    Assert.Equal(HttpStatusCode.OK, inA.StatusCode);
                else
                    await ForbiddenAssert.IsPlanRequiredAsync(inA, Plan.Studio);
            }

            // En su propia organización rige su suscripción studio, sea cual sea el plan de O.
            using var own = await scenario.MemberInOwnOrganization.GetAsync(AuthorizationProbeApiFactory.RequiresStudioPath);
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }

        scenario.DisposeClients();
    }

    // ── CA-98 ───────────────────────────────────────────────────────────────────────────────────────────────────

    // DEC-0104: registrar y listar transacciones es de toda empresa (EmpresaOnly); solo la vista de flujo de caja
    // (GET /stats/cash-flow: mensual, proyección, YTD) es studio.
    [Fact]
    public async Task PLAN0018_CA98_EmpresaStudioBajaAPro_VistaDeFlujoDeCaja403PlanRequiredStudio_TransaccionesIntactasYVisibles_YVuelveAlSubir()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio))
            .ForB(b => b.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro))
            .BuildAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var listPath = $"{CashFlowEndpoint}?year={today.Year}&month={today.Month}";
        var viewPath = $"{CashFlowViewEndpoint}?year={today.Year}&month={today.Month}";
        var organizationId = tenants.A.OrganizationId;
        await tenants.SeedAsync(
            CashFlowEntry.CreateManual(organizationId, "income", "Anticipo del proyecto", 1_500_000m, "COP", 1m, 1_500_000m, today),
            CashFlowEntry.CreateManual(organizationId, "expense", "Licencias", 320_000m, "COP", 1m, 320_000m, today),
            CashFlowEntry.CreateManual(organizationId, "income", "Pago en dólares", 800m, "USD", 4_000m, 3_200_000m, today));

        var snapshot = await CashFlowRowsAsync(organizationId);
        var countsBefore = await RowCountsAsync();
        Assert.Equal(3, snapshot.Count);
        using var http = tenants.ClientFor(tenants.A);

        // studio: la vista responde y las transacciones se listan.
        var viewBefore = await ReadOkAsync(http, viewPath);
        using (var view = JsonDocument.Parse(viewBefore))
        {
            Assert.Equal(1_500_800m, view.RootElement.GetProperty("ytdIngresos").GetDecimal());
            Assert.Equal(320_000m, view.RootElement.GetProperty("ytdEgresos").GetDecimal());
        }
        Assert.Equal(snapshot.Keys.Order(), await ListAsync(http, listPath));

        // Baja a pro (mismo token): la vista responde 403 PLAN_REQUIRED studio en lectura.
        await TenantBuilder.AssignPlanAsync(factory.Services, tenants.A.Owner, Plan.Pro);
        using (var view = await http.GetAsync(viewPath))
            await ForbiddenAssert.IsPlanRequiredAsync(view, Plan.Studio);

        // El registro de transacciones sigue disponible para pro y las filas no cambiaron.
        Assert.Equal(snapshot.Keys.Order(), await ListAsync(http, listPath));
        Assert.Equal(snapshot, await CashFlowRowsAsync(organizationId));

        // Vuelve a studio: la vista devuelve los mismos datos.
        await TenantBuilder.AssignPlanAsync(factory.Services, tenants.A.Owner, Plan.Studio);
        Assert.Equal(viewBefore, await ReadOkAsync(http, viewPath));
        Assert.Equal(snapshot, await CashFlowRowsAsync(organizationId));

        // Ningún conteo de tablas de tenant cambió: solo las que escribe AssignPlanBySeedCommand (suscripción, auditoría).
        var changed = TableRowCounts.Changed(countsBefore, await RowCountsAsync());
        Assert.Empty(changed.Intersect(TenantTables()));
        Assert.All(changed, table => Assert.Contains(table, AssignPlanTables));
    }

    // Contrato de GET /stats/finance tras DEC-0104: solo los indicadores básicos del mes, para cualquier plan (también
    // studio). Lo de la vista de flujo de caja vive únicamente en /stats/cash-flow, detrás de EmpresaStudio.
    [Fact]
    public async Task PLAN0018_CA98_StatsFinance_NoExponeLosCamposDeLaVistaDeFlujoDeCaja_NiAStudio()
    {
        string[] basic = ["egresosMes", "flujoNeto", "ingresosMes"];
        Assert.Equal(
            basic,
            typeof(FinanceStatsDto).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order());

        var studioView = typeof(CashFlowViewDto).GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToList();
        Assert.Contains("monthly", studioView);
        Assert.Contains("projected", studioView);
        Assert.Contains("proyectadoProxMes", studioView);

        foreach (var plan in new[] { Plan.Free, Plan.Pro, Plan.Studio })
        {
            var tenants = await new TenantBuilder(factory)
                .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(plan))
                .BuildAsync();
            using var http = tenants.ClientFor(tenants.A);

            using var body = JsonDocument.Parse(await ReadOkAsync(http, StatsFinanceEndpoint));
            var keys = body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList();
            Assert.Equal(basic, keys);
            Assert.Empty(keys.Intersect(studioView));
        }
    }

    private static async Task<string> ReadOkAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }


    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Tablas que AssignPlanBySeedCommand puede escribir; ninguna es de tenant (no derivan de TenantEntity).
    private static readonly string[] AssignPlanTables =
        [nameof(Subscription), nameof(AuditLog), nameof(BillingCustomer)];

    private IEnumerable<string> TenantTables()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ZentoryDbContext>().Model.GetEntityTypes()
            .Where(e => typeof(TenantEntity).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name)
            .ToList();
    }

    private sealed record CashFlowRow(
        string Type, string Description, decimal Amount, string Currency, decimal ExchangeRate, decimal AmountBase,
        DateOnly TransactionDate, DateTime? DeletedAt);

    private async Task<Dictionary<Guid, CashFlowRow>> CashFlowRowsAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.CashFlowEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.OrganizationId == organizationId)
            .ToDictionaryAsync(e => e.Id, e => new CashFlowRow(
                e.Type, e.Description, e.Amount, e.Currency, e.ExchangeRate, e.AmountBase, e.TransactionDate, e.DeletedAt));
    }

    private async Task<Dictionary<string, int>> RowCountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await TableRowCounts.CountAsync(scope.ServiceProvider.GetRequiredService<ZentoryDbContext>());
    }

    private static async Task<IEnumerable<Guid>> ListAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<List<CashFlowEntryDto>>();
        return rows!.Select(row => Guid.Parse(row.Id)).Order().ToList();
    }

    // O (pro) es owner de A y de una segunda organización B; M es owner de su propia organización (studio, con su
    // suscripción) y member en A. Los tres tokens llevan claim plan = pro y no se renuevan.
    private async Task<OwnerScenario> SeedOwnerWithTwoOrganizationsAndMemberAsync()
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa).WithPlan(Plan.Pro))
            .ForB(b => b.WithLegalType(LegalType.Empresa).WithPlan(Plan.Studio))
            .BuildAsync();
        var owner = tenants.A.Owner;
        var member = tenants.B.Owner;

        var secondOrganization = Organization.Create($"Org A2 {Guid.NewGuid():N}", LegalType.Empresa);
        secondOrganization.SetOwner(owner.UserId);
        await tenants.SeedAsync(
            secondOrganization,
            OrganizationMember.Create(secondOrganization.OrganizationId, owner.UserId, OrgRole.Owner),
            OrganizationMember.Create(tenants.A.OrganizationId, member.UserId, OrgRole.Member, invitedBy: owner.UserId));

        var tokens = factory.Tokens;
        return new OwnerScenario(
            owner,
            Client(tokens.ForUser(owner, tenants.A.Organization, OrgRole.Owner, Plan.Pro)),
            Client(tokens.ForUser(owner, secondOrganization, OrgRole.Owner, Plan.Pro)),
            Client(tokens.ForUser(member, tenants.A.Organization, OrgRole.Member, Plan.Pro)),
            Client(tokens.ForUser(member, tenants.B.Organization, OrgRole.Owner, Plan.Pro)));
    }

    private HttpClient Client(string token)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private sealed record OwnerScenario(
        User       Owner,
        HttpClient OwnerInA,
        HttpClient OwnerInB,
        HttpClient MemberInA,
        HttpClient MemberInOwnOrganization)
    {
        public IEnumerable<(HttpClient Http, string Label)> ClientsInOwnerOrganizations()
            => [(OwnerInA, "O en A"), (OwnerInB, "O en B"), (MemberInA, "M en A")];

        public void DisposeClients()
        {
            OwnerInA.Dispose();
            OwnerInB.Dispose();
            MemberInA.Dispose();
            MemberInOwnOrganization.Dispose();
        }
    }
}

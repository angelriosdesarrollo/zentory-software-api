using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.FinancialVisibility;

// PLAN-0018 CA-115 (RF-82) y CA-126 (RF-86; SR-0018.41; C-14; D-41) sobre el host real, en una organización empresa +
// studio con owner, admin y member (la de CA-52) y un colaborador con HourlyRate y MonthlyRate:
//   - CA-115: auditoría (GET /activity-log) y cada acción de PILA del equipo cerradas al member con 403 ACCESS_DENIED;
//     owner y admin pasan la autorización; empresa + pro sigue en PLAN_REQUIRED para el owner; la PILA personal
//     (/me/ss-calculations) sigue abierta al member.
//   - CA-126: cada acción de flujo de caja y de cuentas de cobro, /stats/profitability, /stats/finance y /stats/cash-flow
//     (deuda de DEC-0105) cerradas al member; colaboradores legibles por el member sin tarifas en el JSON y escritura solo
//     para owner y admin; precedencia tipo → plan → rol de RF-58 (empresa + pro → PLAN_REQUIRED, freelance →
//     ACCESS_DENIED).
// En las celdas denegadas de escritura se compara una instantánea de las filas financieras de la organización antes y
// después: el 403 llega sin efecto. En las permitidas se exige que la petición pase la autorización (ni 401 ni 403 ni
// 5xx); las lecturas de listado y las escrituras con cuerpo válido exigen además 2xx.
public sealed class MemberFinancialAccessTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const decimal HourlyRate  = 85_000m;
    private const decimal MonthlyRate = 6_500_000m;
    private const string  Period      = "2026-09";

    private const string ActivityLogEndpoint     = "/api/v1/activity-log";
    private const string PilaEndpoint            = "/api/v1/team/pila-verifications";
    private const string SsCalculationsEndpoint  = "/api/v1/me/ss-calculations";
    private const string CashFlowEntriesEndpoint = "/api/v1/cash-flow-entries";
    private const string PayoutEndpoint          = "/api/v1/team/payout-invoices";
    private const string CollaboratorsEndpoint   = "/api/v1/collaborators";

    private static readonly string[] Roles = [OrgRole.Owner, OrgRole.Admin, OrgRole.Member];

    // Acciones de PilaVerificationsController (RF-82 b), con los marcadores de los ids sembrados.
    public static readonly string[] PilaActions =
    [
        "GET " + PilaEndpoint + "?period=" + Period,
        "POST " + PilaEndpoint + "/request",
        "GET " + PilaEndpoint + "/{collaboratorId}/history",
        "PATCH " + PilaEndpoint + "/{pilaId}/verify",
        "GET " + PilaEndpoint + "/{pilaId}/download-url",
        "GET " + PilaEndpoint + "/{pilaId}/documents",
        "GET " + PilaEndpoint + "/documents/{randomId}/download-url",
    ];

    // Acciones de CashFlowEntriesController (RF-86 a).
    public static readonly string[] CashFlowActions =
    [
        "GET " + CashFlowEntriesEndpoint,
        "POST " + CashFlowEntriesEndpoint,
        "PATCH " + CashFlowEntriesEndpoint + "/{entryId}",
        "DELETE " + CashFlowEntriesEndpoint + "/{entryId}",
    ];

    // Acciones de PayoutInvoicesController (RF-86 b), incluidas generate, send, download-url, request-manual-upload,
    // suggested-amount, review y project-breakdown.
    public static readonly string[] PayoutActions =
    [
        "GET " + PayoutEndpoint + "?collaboratorId={collaboratorId}",
        "POST " + PayoutEndpoint + "/generate",
        "POST " + PayoutEndpoint + "/{payoutId}/send",
        "GET " + PayoutEndpoint + "/{payoutId}/download-url",
        "POST " + PayoutEndpoint + "/request-manual-upload",
        "GET " + PayoutEndpoint + "/suggested-amount?collaboratorId={collaboratorId}&period=" + Period,
        "PATCH " + PayoutEndpoint + "/{payoutId}/review",
        "GET " + PayoutEndpoint + "/{payoutId}/project-breakdown",
    ];

    public static readonly string[] FinancialStatsEndpoints =
    [
        "/api/v1/stats/profitability",
        "/api/v1/stats/finance",
        "/api/v1/stats/cash-flow",
    ];

    public static TheoryData<string, string> PilaCells     => Cells(PilaActions);
    public static TheoryData<string, string> CashFlowCells => Cells(CashFlowActions);
    public static TheoryData<string, string> PayoutCells   => Cells(PayoutActions);
    public static TheoryData<string, string> StatsCells    => Cells(FinancialStatsEndpoints);

    // ── CA-115: auditoría y PILA del equipo (RF-82) ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA115_ActivityLog_OwnerYAdmin200_Member403AccessDenied(string role)
    {
        var arranged = await ArrangeAsync(role);

        using var response = await arranged.Http.GetAsync(ActivityLogEndpoint);

        if (role == OrgRole.Member)
            await ForbiddenAssert.IsAccessDeniedAsync(response);
        else
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(PilaCells))]
    public async Task PLAN0018_CA115_PilaDelEquipo_CadaAccion_Member403AccessDeniedSinEfecto_OwnerYAdminPasan(
        string action, string role)
    {
        await AssertCellAsync(action, role);
    }

    [Fact]
    public async Task PLAN0018_CA115_PilaDelEquipo_EmpresaPro_Owner403PlanRequiredComoHoy()
    {
        var arranged = await ArrangeAsync(OrgRole.Owner, plan: Plan.Pro);

        foreach (var action in PilaActions)
        {
            using var response = await SendAsync(arranged, action);
            await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Studio);
        }
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA115_SsCalculationsPersonal_Responde200AOwnerAdminYMember(string role)
    {
        var arranged = await ArrangeAsync(role);

        using var response = await arranged.Http.GetAsync(SsCalculationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA115_PilaDelEquipo_ListadoParaOwnerYAdmin_Responde200()
    {
        foreach (var role in new[] { OrgRole.Owner, OrgRole.Admin })
        {
            var arranged = await ArrangeAsync(role);
            using var response = await SendAsync(arranged, PilaActions[0]);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role}: {(int)response.StatusCode}");
        }
    }

    // ── CA-126: flujo de caja, cuentas de cobro y estadísticas financieras (RF-86 a, b, c) ─────────────────────

    [Theory]
    [MemberData(nameof(CashFlowCells))]
    public async Task PLAN0018_CA126_FlujoDeCaja_CadaAccion_Member403AccessDeniedSinEfecto_OwnerYAdminPasan(
        string action, string role)
    {
        await AssertCellAsync(action, role);
    }

    [Theory]
    [MemberData(nameof(PayoutCells))]
    public async Task PLAN0018_CA126_CuentasDeCobro_CadaAccion_Member403AccessDeniedSinEfecto_OwnerYAdminPasan(
        string action, string role)
    {
        await AssertCellAsync(action, role);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    public async Task PLAN0018_CA126_FlujoDeCaja_OwnerYAdmin_ListanYRegistranConExito(string role)
    {
        // El registro y listado de transacciones sigue disponible para quien tiene acceso financiero (DEC-0104).
        var arranged = await ArrangeAsync(role);

        using var list = await SendAsync(arranged, CashFlowActions[0]);
        using var create = await SendAsync(arranged, CashFlowActions[1]);
        using var delete = await SendAsync(arranged, CashFlowActions[3]);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Theory]
    [MemberData(nameof(StatsCells))]
    public async Task PLAN0018_CA126_EstadisticasFinancieras_OwnerYAdmin200_Member403AccessDenied(string path, string role)
    {
        var arranged = await ArrangeAsync(role);

        using var response = await arranged.Http.GetAsync(path);

        if (role == OrgRole.Member)
            await ForbiddenAssert.IsAccessDeniedAsync(response);
        else
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} en GET {path}: {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("/api/v1/stats/projects")]
    [InlineData("/api/v1/stats/invoices")]
    public async Task PLAN0018_CA126_StatsProjectsEInvoices_Member200(string path)
    {
        var arranged = await ArrangeAsync(OrgRole.Member);

        using var response = await arranged.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── CA-126: precedencia tipo → plan → rol (RF-58) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA126_Precedencia_MemberEmpresaPro_CuentasDeCobro_403PlanRequired()
    {
        // Fallan plan (studio) y rol a la vez: gana el plan.
        var arranged = await ArrangeAsync(OrgRole.Member, plan: Plan.Pro);

        foreach (var action in PayoutActions)
        {
            using var response = await SendAsync(arranged, action);
            await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Studio);
        }
    }

    [Fact]
    public async Task PLAN0018_CA126_Precedencia_MemberEmpresaPro_VistaDeFlujoDeCaja_403PlanRequired()
    {
        // Deuda de DEC-0105: /stats/cash-flow suma RequireFinancialAccess a EmpresaStudio; con los dos fallando gana el plan.
        var arranged = await ArrangeAsync(OrgRole.Member, plan: Plan.Pro);

        using var response = await arranged.Http.GetAsync("/api/v1/stats/cash-flow");

        await ForbiddenAssert.IsPlanRequiredAsync(response, Plan.Studio);
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA126_Precedencia_Freelance_CuentasDeCobroYFlujoDeCaja_403AccessDenied(string role)
    {
        // El tipo de cuenta gana al plan y al rol: freelance studio recibe ACCESS_DENIED.
        var arranged = await ArrangeAsync(role, legalType: LegalType.Freelance);

        foreach (var action in PayoutActions.Concat(CashFlowActions))
        {
            using var response = await SendAsync(arranged, action);
            await ForbiddenAssert.IsAccessDeniedAsync(response);
        }
    }

    // ── CA-126: colaboradores sin tarifas para member (RF-86 d) ─────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA126_Colaboradores_ListaYDetalle_200_MemberSinTarifas_OwnerYAdminConTarifas(string role)
    {
        var arranged = await ArrangeAsync(role);
        var canSeeRates = role != OrgRole.Member;

        using var list = await arranged.Http.GetAsync(CollaboratorsEndpoint);
        using var detail = await arranged.Http.GetAsync($"{CollaboratorsEndpoint}/{arranged.CollaboratorId}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var item = Assert.Single(listJson.RootElement.EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == arranged.CollaboratorId);
        AssertRates(item, canSeeRates);

        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertRates(detailJson.RootElement, canSeeRates);
        Assert.Equal("COP", detailJson.RootElement.GetProperty("currency").GetString());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("PATCH-RATE")]
    [InlineData("DELETE")]
    public async Task PLAN0018_CA126_Colaboradores_Escritura_Member403AccessDeniedSinCambios(string write)
    {
        var arranged = await ArrangeAsync(OrgRole.Member);
        var before = await SnapshotAsync(arranged.OrganizationId);

        using var response = await CollaboratorWriteAsync(arranged, write);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(before, await SnapshotAsync(arranged.OrganizationId));
    }

    [Theory]
    [InlineData(OrgRole.Owner, "POST", HttpStatusCode.Created)]
    [InlineData(OrgRole.Owner, "PATCH", HttpStatusCode.OK)]
    [InlineData(OrgRole.Owner, "PATCH-RATE", HttpStatusCode.OK)]
    [InlineData(OrgRole.Owner, "DELETE", HttpStatusCode.NoContent)]
    [InlineData(OrgRole.Admin, "POST", HttpStatusCode.Created)]
    [InlineData(OrgRole.Admin, "PATCH", HttpStatusCode.OK)]
    [InlineData(OrgRole.Admin, "PATCH-RATE", HttpStatusCode.OK)]
    [InlineData(OrgRole.Admin, "DELETE", HttpStatusCode.NoContent)]
    public async Task PLAN0018_CA126_Colaboradores_Escritura_OwnerYAdmin2xxConEfecto(string role, string write, HttpStatusCode expected)
    {
        var arranged = await ArrangeAsync(role);
        var before = await SnapshotAsync(arranged.OrganizationId);

        using var response = await CollaboratorWriteAsync(arranged, write);

        Assert.Equal(expected, response.StatusCode);
        Assert.NotEqual(before, await SnapshotAsync(arranged.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA126_Colaboradores_PatchSoloTarifa_Admin_CambiaLaTarifaYLaDevuelve()
    {
        var arranged = await ArrangeAsync(OrgRole.Admin);

        using var response = await CollaboratorWriteAsync(arranged, "PATCH-RATE");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HourlyRate + 1, json.RootElement.GetProperty("hourlyRate").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var stored = await db.Collaborators.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == arranged.CollaboratorId);
        Assert.Equal(HourlyRate + 1, stored.HourlyRate);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Arranged(
        HttpClient Http,
        Guid OrganizationId,
        Guid CollaboratorId,
        Guid PilaId,
        Guid PayoutId,
        Guid EntryId);

    private static TheoryData<string, string> Cells(IEnumerable<string> actions)
    {
        var data = new TheoryData<string, string>();
        foreach (var action in actions)
        foreach (var role in Roles)
            data.Add(action, role);
        return data;
    }

    // Organización A con owner, admin y member, y en ella un colaborador con tarifas, una verificación PILA, una cuenta de
    // cobro y una transacción manual; devuelve el cliente del rol pedido.
    private async Task<Arranged> ArrangeAsync(string role, string? plan = null, string? legalType = null)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(legalType ?? LegalType.Empresa).WithPlan(plan ?? Plan.Studio)
                .WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .BuildAsync();
        var org = tenants.A.OrganizationId;

        var collaborator = Collaborator.Create(org, "Colaborador con tarifas", "hourly_contractor",
            email: $"colaborador-{Guid.NewGuid():N}@example.test", hourlyRate: HourlyRate, monthlyRate: MonthlyRate);
        var pila = PilaVerification.Create(org, collaborator.Id, Period);
        var payout = CollaboratorPayoutInvoice.Create(org, collaborator.Id, Period, "Servicios de septiembre", 1_000_000m);
        var entry = CashFlowEntry.CreateManual(org, "expense", "Arriendo", 2_000_000m, "COP", 1m, 2_000_000m,
            new DateOnly(2026, 9, 1));
        await tenants.SeedAsync(collaborator, pila, payout, entry);

        var actor = role == OrgRole.Owner ? tenants.A.Owner : tenants.A.Member(role);
        return new Arranged(tenants.ClientFor(tenants.A, actor), org, collaborator.Id, pila.Id, payout.Id, entry.Id);
    }

    // Celda de la matriz: member → 403 ACCESS_DENIED sin efecto; owner y admin pasan la autorización.
    private async Task AssertCellAsync(string action, string role)
    {
        var arranged = await ArrangeAsync(role);
        var before = await SnapshotAsync(arranged.OrganizationId);

        using var response = await SendAsync(arranged, action);

        if (role == OrgRole.Member)
        {
            await ForbiddenAssert.IsAccessDeniedAsync(response);
            Assert.Equal(before, await SnapshotAsync(arranged.OrganizationId));
        }
        else
        {
            var status = (int)response.StatusCode;
            Assert.True(status is not (401 or 403) && status < 500, $"{role} en {action}: {status}");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Arranged arranged, string action)
    {
        var space = action.IndexOf(' ');
        var method = new HttpMethod(action[..space]);
        var path = action[(space + 1)..]
            .Replace("{collaboratorId}", arranged.CollaboratorId.ToString())
            .Replace("{pilaId}", arranged.PilaId.ToString())
            .Replace("{payoutId}", arranged.PayoutId.ToString())
            .Replace("{entryId}", arranged.EntryId.ToString())
            .Replace("{randomId}", Guid.NewGuid().ToString());

        using var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
            request.Content = new StringContent(BodyFor(action, arranged), Encoding.UTF8, "application/json");
        return await arranged.Http.SendAsync(request);
    }

    // Cuerpo válido para las escrituras de flujo de caja (las de owner y admin llegan a 2xx); para el resto basta un
    // objeto vacío: el 403 del member sale antes del enlace del modelo y owner y admin solo deben pasar la autorización.
    private static string BodyFor(string action, Arranged arranged)
    {
        if (action.StartsWith("POST " + CashFlowEntriesEndpoint, StringComparison.Ordinal)
            || action.StartsWith("PATCH " + CashFlowEntriesEndpoint, StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                type = "income",
                description = "Pago de cliente",
                amount = 1_500_000m,
                currency = "COP",
                transactionDate = "2026-09-15",
                exchangeRate = 1m,
            });
        }

        return "{}";
    }

    private static Task<HttpResponseMessage> CollaboratorWriteAsync(Arranged arranged, string write)
    {
        var path = $"{CollaboratorsEndpoint}/{arranged.CollaboratorId}";
        return write switch
        {
            "POST" => arranged.Http.PostAsJsonAsync(CollaboratorsEndpoint, new
            {
                name = "Nuevo colaborador",
                type = "hourly_contractor",
                currency = "COP",
                hourlyRate = 50_000m,
            }),
            "PATCH" => arranged.Http.PatchAsJsonAsync(path, UpdateBody("Colaborador renombrado", HourlyRate)),
            "PATCH-RATE" => arranged.Http.PatchAsJsonAsync(path, UpdateBody("Colaborador con tarifas", HourlyRate + 1)),
            "DELETE" => arranged.Http.DeleteAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(write), write, null),
        };
    }

    // Mismo estado que el sembrado salvo lo que cambia la celda: "PATCH-RATE" solo cambia la tarifa por hora.
    private static object UpdateBody(string name, decimal hourlyRate) => new
    {
        name,
        type = "hourly_contractor",
        status = "active",
        currency = "COP",
        hourlyRate,
        monthlyRate = MonthlyRate,
    };

    // Para quien ve montos, el JSON trae las dos tarifas; para member no las trae (ni en cero ni en null).
    private static void AssertRates(JsonElement collaborator, bool visible)
    {
        Assert.Equal("Colaborador con tarifas", collaborator.GetProperty("name").GetString());

        if (visible)
        {
            Assert.Equal(HourlyRate, collaborator.GetProperty("hourlyRate").GetDecimal());
            Assert.Equal(MonthlyRate, collaborator.GetProperty("monthlyRate").GetDecimal());
        }
        else
        {
            Assert.False(collaborator.TryGetProperty("hourlyRate", out _), "member recibió hourlyRate");
            Assert.False(collaborator.TryGetProperty("monthlyRate", out _), "member recibió monthlyRate");
        }
    }

    // Instantánea de las filas financieras de la organización: colaboradores (tarifas y baja), verificaciones PILA,
    // cuentas de cobro y transacciones, con su UpdatedAt.
    private async Task<string> SnapshotAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();

        var collaborators = await db.Collaborators.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.OrganizationId == organizationId).OrderBy(c => c.Id)
            .Select(c => $"{c.Id}|{c.Name}|{c.HourlyRate}|{c.MonthlyRate}|{c.Status}|{c.DeletedAt}|{c.UpdatedAt:O}")
            .ToListAsync();
        var pila = await db.PilaVerifications.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrganizationId == organizationId).OrderBy(p => p.Id)
            .Select(p => $"{p.Id}|{p.Status}|{p.UpdatedAt:O}")
            .ToListAsync();
        var payouts = await db.CollaboratorPayoutInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrganizationId == organizationId).OrderBy(p => p.Id)
            .Select(p => $"{p.Id}|{p.Status}|{p.Amount}|{p.DeletedAt}|{p.UpdatedAt:O}")
            .ToListAsync();
        var entries = await db.CashFlowEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.OrganizationId == organizationId).OrderBy(e => e.Id)
            .Select(e => $"{e.Id}|{e.Amount}|{e.Description}|{e.DeletedAt}|{e.UpdatedAt:O}")
            .ToListAsync();

        return string.Join("\n", collaborators.Concat(pila).Concat(payouts).Concat(entries));
    }
}

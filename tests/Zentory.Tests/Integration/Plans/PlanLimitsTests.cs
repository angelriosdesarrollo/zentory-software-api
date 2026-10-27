using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding.Demo;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Plans;

// PLAN-0018 T25b (RF-41, RF-74, RF-75 d; D-30, C-9; SR-0018.35; FEATURE_PERMISSIONS §3) sobre el host real, con el
// catálogo de ReferenceDataSeeder y los escenarios demo sembrados en la base de la fixture.
//
// CA-46: los 6 estados legalType × plan contra los límites de §3 (clientes, facturas del mes, organizaciones propias,
// proyectos sin límite y GET /organization/plan-limits). El límite de miembros lo verifica CA-58.
// CA-97: ámbito de los límites. Los cuantitativos son por organización con el plan de su owner; max_owned_orgs es por
// owner con su plan propio, nunca el de la organización activa (también corrige H-1 de TK-0060: un owner pro o studio
// recibía 422 al crear su 2.ª organización porque POST /organizations no tiene plan efectivo).
// CA-T25B-1 (parte de CA-88): la Consultora del escenario demo, por encima de los límites de free tras bajar de pro.
public sealed class PlanLimitsTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string ClientsEndpoint       = "/api/v1/clients";
    private const string InvoicesEndpoint      = "/api/v1/invoices";
    private const string ProjectsEndpoint      = "/api/v1/projects";
    private const string OrganizationsEndpoint = "/api/v1/organizations";
    private const string PlanLimitsEndpoint    = "/api/v1/organization/plan-limits";
    private const string MembersEndpoint       = "/api/v1/organization/members";

    // Límites de §3 que el seed de referencia debe reflejar.
    private const int FreeClients      = 2;
    private const int FreeInvoices     = 3;
    private const int FreeOwnedOrgs    = 1;
    private const int ProjectsToCreate = 6;

    public static TheoryData<string, string> SixStates()
    {
        var data = new TheoryData<string, string>();
        foreach (var legalType in new[] { LegalType.Freelance, LegalType.Empresa })
            foreach (var plan in new[] { Plan.Free, Plan.Pro, Plan.Studio })
                data.Add(legalType, plan);
        return data;
    }

    // ── CA-46 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SixStates))]
    public async Task PLAN0018_CA46_TercerCliente_EnFreeResponde422QuotaExceeded_EnProYStudioSeCrea(string legalType, string plan)
    {
        var tenant = await SeedOwnerAsync(legalType, plan);
        using var http = ClientFor(tenant.Owner, tenant.Organization);

        for (var i = 0; i < FreeClients; i++)
            await CreateClientAsync(http);

        using var third = await PostClientAsync(http);

        if (plan == Plan.Free)
            await AssertQuotaExceededAsync(third, PlanLimits.FeatureKeys.MaxClients);
        else
            Assert.Equal(HttpStatusCode.Created, third.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SixStates))]
    public async Task PLAN0018_CA46_CuartaFacturaDelMes_EnFreeResponde422QuotaExceeded_EnProYStudioSeCrea(string legalType, string plan)
    {
        var tenant = await SeedOwnerAsync(legalType, plan);
        using var http = ClientFor(tenant.Owner, tenant.Organization);
        var clientId = await CreateClientAsync(http);

        for (var i = 0; i < FreeInvoices; i++)
        {
            using var created = await PostInvoiceAsync(http, clientId);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var fourth = await PostInvoiceAsync(http, clientId);

        if (plan == Plan.Free)
            await AssertQuotaExceededAsync(fourth, PlanLimits.FeatureKeys.MaxInvoicesMonth);
        else
            Assert.Equal(HttpStatusCode.Created, fourth.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SixStates))]
    public async Task PLAN0018_CA46_SegundaOrganizacionPropia_EnFreeResponde422QuotaExceeded_EnProYStudioSeCrea(string legalType, string plan)
    {
        var tenant = await SeedOwnerAsync(legalType, plan);
        using var http = ClientFor(tenant.Owner, tenant.Organization);

        using var second = await PostOrganizationAsync(http, legalType);

        if (plan == Plan.Free)
            await AssertQuotaExceededAsync(second, PlanLimits.FeatureKeys.MaxOwnedOrgs);
        else
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(plan == Plan.Free ? FreeOwnedOrgs : FreeOwnedOrgs + 1, await OwnedCountAsync(tenant.Owner.UserId));
    }

    [Theory]
    [MemberData(nameof(SixStates))]
    public async Task PLAN0018_CA46_SextoProyecto_SeCreaEnLosSeisEstados_IncluidoFree(string legalType, string plan)
    {
        var tenant = await SeedOwnerAsync(legalType, plan);
        using var http = ClientFor(tenant.Owner, tenant.Organization);
        var clientId = await CreateClientAsync(http);

        for (var i = 1; i <= ProjectsToCreate; i++)
        {
            using var response = await http.PostAsJsonAsync(ProjectsEndpoint, new
            {
                name = $"Proyecto {i} {Guid.NewGuid():N}", clientId, billingType = nameof(BillingType.FixedPrice),
                contractValue = 1_000m, currency = "COP",
            });
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"proyecto {i}: {(int)response.StatusCode}");
        }
    }

    [Theory]
    [MemberData(nameof(SixStates))]
    public async Task PLAN0018_CA46_GetPlanLimits_DevuelveLosValoresDeFeaturePermissions3_ConMaxProjectsSinLimite(string legalType, string plan)
    {
        var tenant = await SeedOwnerAsync(legalType, plan);
        using var http = ClientFor(tenant.Owner, tenant.Organization);

        var limits = await PlanLimitsAsync(http);

        var free = plan == Plan.Free;
        Assert.Equal(free ? FreeClients : null, limits.MaxClients);
        Assert.Equal(free ? FreeInvoices : null, limits.MaxInvoicesMonth);
        Assert.Equal(ExpectedMembers(legalType, plan), limits.MaxOrgMembers);
        Assert.Null(limits.MaxProjects);
    }

    // ── CA-97 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA97_OwnerFreeConDosClientesEnA_ComoMemberDeBDeOtroOwnerFree_CreaClienteEnBPorqueElCupoEsDeB()
    {
        var o = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        var b = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        await AddMemberAsync(b.Organization, o.Owner, b.Owner);
        using var inA = ClientFor(o.Owner, o.Organization);
        using var inB = ClientFor(o.Owner, b.Organization);
        for (var i = 0; i < FreeClients; i++)
            await CreateClientAsync(inA);

        using (var thirdInA = await PostClientAsync(inA))
            await AssertQuotaExceededAsync(thirdInA, PlanLimits.FeatureKeys.MaxClients);
        using var inBResponse = await PostClientAsync(inB);

        Assert.Equal(HttpStatusCode.Created, inBResponse.StatusCode);
        Assert.Equal(FreeClients, await ClientCountAsync(o.Organization.OrganizationId));
        Assert.Equal(1, await ClientCountAsync(b.Organization.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA97_OwnerFreeMemberEnB_NoCreaSegundaOrganizacionPropia_DesdeANiDesdeB()
    {
        var o = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        var b = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        await AddMemberAsync(b.Organization, o.Owner, b.Owner);

        foreach (var active in new[] { o.Organization, b.Organization })
        {
            using var http = ClientFor(o.Owner, active);
            using var response = await PostOrganizationAsync(http, LegalType.Empresa);
            await AssertQuotaExceededAsync(response, PlanLimits.FeatureKeys.MaxOwnedOrgs);
        }
        Assert.Equal(FreeOwnedOrgs, await OwnedCountAsync(o.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA97_OwnerFreeConCDeUnOwnerStudioComoActiva_NoCreaSegundaOrganizacionPropia_UsaSuPlanPropio()
    {
        // C-9 / SR-0018.35: el plan de C (studio, max_owned_orgs ilimitado) no cuenta; rige el plan propio de O (free).
        var o = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        var c = await SeedOwnerAsync(LegalType.Empresa, Plan.Studio);
        await AddMemberAsync(c.Organization, o.Owner, c.Owner);
        Assert.Equal(Plan.Studio, await PlanOfOwnerAsync(c.Owner.UserId));
        using var http = ClientFor(o.Owner, c.Organization);

        using var response = await PostOrganizationAsync(http, LegalType.Empresa);

        await AssertQuotaExceededAsync(response, PlanLimits.FeatureKeys.MaxOwnedOrgs);
        Assert.Equal(FreeOwnedOrgs, await OwnedCountAsync(o.Owner.UserId));
    }

    // H-1 de TK-0060: POST /organizations no tiene plan efectivo (SkipMembershipValidation); el límite sale del plan propio.
    [Theory]
    [InlineData(Plan.Pro)]
    [InlineData(Plan.Studio)]
    public async Task PLAN0018_CA97_OwnerProOStudio_CreaSegundaYTerceraOrganizacionPropia_DesdeSuOrganizacionActiva(string plan)
    {
        var o = await SeedOwnerAsync(LegalType.Empresa, plan);
        using var http = ClientFor(o.Owner, o.Organization);

        using (var second = await PostOrganizationAsync(http, LegalType.Empresa))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using (var third = await PostOrganizationAsync(http, LegalType.Freelance))
            Assert.Equal(HttpStatusCode.OK, third.StatusCode);

        Assert.Equal(3, await OwnedCountAsync(o.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA97_TrasSubirAPro_CreaSegundaOrganizacionPropia_YCadaUnaTieneSuPropioConteoDeClientesYFacturas()
    {
        var o = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        using var inA = ClientFor(o.Owner, o.Organization);
        var clientInA = await CreateClientAsync(inA);
        await CreateClientAsync(inA);
        for (var i = 0; i < FreeInvoices; i++)
        {
            using var invoice = await PostInvoiceAsync(inA, clientInA);
            Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        }

        await TenantBuilder.AssignPlanAsync(factory.Services, o.Owner, Plan.Pro);
        using var created = await PostOrganizationAsync(inA, LegalType.Empresa);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var inSecond = await ClientFromSwitchResponseAsync(created);

        // De vuelta en free, cada organización conserva su propio cupo: A está llena y la segunda empieza en cero.
        await TenantBuilder.AssignPlanAsync(factory.Services, o.Owner, Plan.Free);
        using (var blockedClient = await PostClientAsync(inA))
            await AssertQuotaExceededAsync(blockedClient, PlanLimits.FeatureKeys.MaxClients);
        using (var blockedInvoice = await PostInvoiceAsync(inA, clientInA))
            await AssertQuotaExceededAsync(blockedInvoice, PlanLimits.FeatureKeys.MaxInvoicesMonth);

        var clientInSecond = await CreateClientAsync(inSecond);
        await CreateClientAsync(inSecond);
        using (var invoice = await PostInvoiceAsync(inSecond, clientInSecond))
            Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        using (var thirdInSecond = await PostClientAsync(inSecond))
            await AssertQuotaExceededAsync(thirdInSecond, PlanLimits.FeatureKeys.MaxClients);
        Assert.Equal(2, await OwnedCountAsync(o.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA97_MaxOrgMembers_SeLeePorOrganizacionConElPlanDeSuOwner_NoConElDelUsuario()
    {
        // O es free; en su organización ve 1, como member de una empresa pro ve 5 y en una studio, sin límite.
        var o = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        var pro = await SeedOwnerAsync(LegalType.Empresa, Plan.Pro);
        var studio = await SeedOwnerAsync(LegalType.Empresa, Plan.Studio);
        await AddMemberAsync(pro.Organization, o.Owner, pro.Owner);
        await AddMemberAsync(studio.Organization, o.Owner, studio.Owner);

        var expected = new (Organization Organization, int? Members)[]
        {
            (o.Organization, ExpectedMembers(LegalType.Empresa, Plan.Free)),
            (pro.Organization, ExpectedMembers(LegalType.Empresa, Plan.Pro)),
            (studio.Organization, ExpectedMembers(LegalType.Empresa, Plan.Studio)),
        };
        foreach (var (organization, members) in expected)
        {
            using var http = ClientFor(o.Owner, organization);
            Assert.Equal(members, (await PlanLimitsAsync(http)).MaxOrgMembers);
        }
    }

    // El cupo de clientes y facturas es el de la organización (plan de su owner), nunca el plan propio del que llama.
    [Fact]
    public async Task PLAN0018_CA97_MemberFreeEnOrganizacionPro_CreaTercerClienteYCuartaFacturaDelMes_PorqueCuentaElPlanDeLaOrganizacion()
    {
        var member = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        var pro = await SeedOwnerAsync(LegalType.Empresa, Plan.Pro);
        await AddMemberAsync(pro.Organization, member.Owner, pro.Owner);
        Assert.Equal(Plan.Free, await PlanOfOwnerAsync(member.Owner.UserId));
        using var http = ClientFor(member.Owner, pro.Organization);

        var clientId = await CreateClientAsync(http);
        await CreateClientAsync(http);
        using (var third = await PostClientAsync(http))
            Assert.Equal(HttpStatusCode.Created, third.StatusCode);

        for (var i = 0; i < FreeInvoices; i++)
        {
            using var invoice = await PostInvoiceAsync(http, clientId);
            Assert.Equal(HttpStatusCode.Created, invoice.StatusCode);
        }
        using (var fourth = await PostInvoiceAsync(http, clientId))
            Assert.Equal(HttpStatusCode.Created, fourth.StatusCode);

        Assert.Equal(FreeClients + 1, await ClientCountAsync(pro.Organization.OrganizationId));
        Assert.Equal(FreeInvoices + 1, await InvoicesThisMonthAsync(pro.Organization.OrganizationId));
    }

    // Espejo: el plan pro propio del que llama no amplía el cupo de una organización cuyo owner es free.
    [Fact]
    public async Task PLAN0018_CA97_OwnerProComoMemberEnOrganizacionFree_Recibe422AlTercerCliente_PorqueCuentaElPlanDeLaOrganizacion()
    {
        var member = await SeedOwnerAsync(LegalType.Empresa, Plan.Pro);
        var free = await SeedOwnerAsync(LegalType.Empresa, Plan.Free);
        await AddMemberAsync(free.Organization, member.Owner, free.Owner);
        Assert.Equal(Plan.Pro, await PlanOfOwnerAsync(member.Owner.UserId));
        using var http = ClientFor(member.Owner, free.Organization);

        for (var i = 0; i < FreeClients; i++)
            await CreateClientAsync(http);
        using var third = await PostClientAsync(http);

        await AssertQuotaExceededAsync(third, PlanLimits.FeatureKeys.MaxClients);
        Assert.Equal(FreeClients, await ClientCountAsync(free.Organization.OrganizationId));
    }

    // ── CA-T25B-1 (parte de CA-88): la Consultora del escenario demo ────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA88_Consultora_ConservaTresClientesCuatroFacturasDelMesDosMiembrosYLaFacturaEnUsd_Visibles()
    {
        var (owner, consultancy, _) = await DemoAsync();
        using var http = ClientFor(owner, consultancy);

        using var clients = await GetJsonAsync(http, ClientsEndpoint);
        using var invoices = await GetJsonAsync(http, InvoicesEndpoint);

        Assert.Equal(3, clients.RootElement.GetArrayLength());
        var currencies = invoices.RootElement.EnumerateArray().Select(i => i.GetProperty("currency").GetString()).ToList();
        Assert.Equal(4, currencies.Count);
        Assert.Contains("USD", currencies);
        Assert.Equal(4, await InvoicesThisMonthAsync(ConsultancyScenario.OrganizationId));

        // Los 2 miembros se conservan (RF-75: la bajada conserva y bloquea). Desde T27 la lista de miembros es
        // ManageMembers (pro, RF-47), así que en free el owner recibe PLAN_REQUIRED y la conservación se mira en la base.
        Assert.Equal(2, await ActiveMembersAsync(ConsultancyScenario.OrganizationId));
        using var members = await http.GetAsync(MembersEndpoint);
        await ForbiddenAssert.IsPlanRequiredAsync(members, Plan.Pro);
    }

    [Fact]
    public async Task PLAN0018_CA88_OwnerDeLaConsultora_Recibe422AlCrearClienteFacturaOSegundaOrganizacion_TambienConLaAgenciaActiva()
    {
        var (owner, consultancy, agency) = await DemoAsync();
        Assert.Equal(Plan.Studio, await PlanOfOwnerAsync(agency.OwnerId!.Value));
        using var inConsultancy = ClientFor(owner, consultancy);
        using var inAgency = ClientFor(owner, agency);
        var clientId = await FirstClientIdAsync(ConsultancyScenario.OrganizationId);

        using (var client = await PostClientAsync(inConsultancy))
            await AssertQuotaExceededAsync(client, PlanLimits.FeatureKeys.MaxClients);
        using (var invoice = await PostInvoiceAsync(inConsultancy, clientId))
            await AssertQuotaExceededAsync(invoice, PlanLimits.FeatureKeys.MaxInvoicesMonth);
        using (var fromConsultancy = await PostOrganizationAsync(inConsultancy, LegalType.Empresa))
            await AssertQuotaExceededAsync(fromConsultancy, PlanLimits.FeatureKeys.MaxOwnedOrgs);
        using (var fromAgency = await PostOrganizationAsync(inAgency, LegalType.Empresa))
            await AssertQuotaExceededAsync(fromAgency, PlanLimits.FeatureKeys.MaxOwnedOrgs);

        Assert.Equal(FreeOwnedOrgs, await OwnedCountAsync(owner.UserId));
        Assert.Equal(3, await ClientCountAsync(ConsultancyScenario.OrganizationId));
        Assert.Equal(4, await InvoicesThisMonthAsync(ConsultancyScenario.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA88_OwnerDeLaConsultora_TieneSubscriptionProCanceladaPorDowngradedBySeed_NingunaActiva_YMembresiaVigenteEnLaAgencia()
    {
        await DemoAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var ownerId = ConsultancyScenario.Owner.Id;

        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.UserId == ownerId)
            .Join(db.BillingPlans, s => s.PlanId, p => p.Id, (s, p) => new { s.Status, s.CancellationReason, Plan = p.Name })
            .ToListAsync();

        var cancelled = Assert.Single(subscriptions);
        Assert.Equal(Plan.Pro, cancelled.Plan);
        Assert.Equal(SubscriptionStatus.Cancelled, cancelled.Status);
        Assert.Equal(SubscriptionCancellationReason.DowngradedBySeed, cancelled.CancellationReason);
        Assert.DoesNotContain(subscriptions, s => SubscriptionStatus.Current.Contains(s.Status));

        var agencyMembership = await db.OrganizationMembers.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(m => m.OrganizationId == AgencyScenario.OrganizationId && m.UserId == ownerId && m.DeletedAt == null);
        Assert.Equal(OrgRole.Member, agencyMembership.Role);
    }

    [Fact]
    public async Task PLAN0018_CA88_OwnerDeLaFreelanceNueva_NoTieneClientesNiProyectosNiProfileWorkType()
    {
        await DemoAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organizationId = FreelanceNewScenario.OrganizationId;

        var ownerId = await db.Organizations.IgnoreQueryFilters()
            .Where(o => o.OrganizationId == organizationId).Select(o => o.OwnerId).SingleAsync();
        var owned = await db.Organizations.IgnoreQueryFilters().Where(o => o.OwnerId == ownerId)
            .Select(o => o.OrganizationId).ToListAsync();

        Assert.Equal([organizationId], owned);
        Assert.False(await db.Clients.IgnoreQueryFilters().AnyAsync(c => owned.Contains(c.OrganizationId)));
        Assert.False(await db.Projects.IgnoreQueryFilters().AnyAsync(p => owned.Contains(p.OrganizationId)));
        Assert.False(await db.OrganizationSettings.IgnoreQueryFilters()
            .AnyAsync(s => owned.Contains(s.OrganizationId) && s.Key == OrgSettingKey.ProfileWorkType));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Limits(int? MaxClients, int? MaxInvoicesMonth, int? MaxOrgMembers, int? MaxProjects);

    // §3 "Miembros por organización" (1 · 5 · ilimitados) es de empresa; un freelance no tiene miembros (siempre 1).
    private static int? ExpectedMembers(string legalType, string plan)
        => legalType == LegalType.Freelance || plan == Plan.Free ? 1 : plan == Plan.Pro ? 5 : null;

    private Task EnsureSeededAsync() => DemoScenarioSeed.EnsureAsync(factory.Services);

    private async Task<SeededTenant> SeedOwnerAsync(string legalType, string plan)
    {
        await EnsureSeededAsync();
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithLegalType(legalType).WithPlan(plan)).BuildAsync();
        return tenants.A;
    }

    private async Task<(User Owner, Organization Consultancy, Organization Agency)> DemoAsync()
    {
        await EnsureSeededAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var owner = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == ConsultancyScenario.Owner.Id);
        var consultancy = await db.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == ConsultancyScenario.OrganizationId);
        var agency = await db.Organizations.AsNoTracking().SingleAsync(o => o.OrganizationId == AgencyScenario.OrganizationId);
        Assert.Equal(owner.UserId, consultancy.OwnerId);
        return (owner, consultancy, agency);
    }

    private async Task AddMemberAsync(Organization organization, User member, User invitedBy)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.OrganizationMembers.Add(OrganizationMember.Create(
            organization.OrganizationId, member.UserId, OrgRole.Member, invitedBy: invitedBy.UserId));
        await db.SaveChangesAsync();
    }

    // JWT real del usuario con esa organización activa; el rol y el plan los resuelve el middleware desde la base.
    private HttpClient ClientFor(User user, Organization activeOrganization)
    {
        var role = activeOrganization.OwnerId == user.UserId ? OrgRole.Owner : OrgRole.Member;
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.Tokens.ForUser(user, activeOrganization, role));
        return http;
    }

    // POST /organizations devuelve el access token activo en la organización nueva (mismo contrato que switch).
    private async Task<HttpClient> ClientFromSwitchResponseAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString());
        return http;
    }

    private static Task<HttpResponseMessage> PostClientAsync(HttpClient http)
        => http.PostAsJsonAsync(ClientsEndpoint, new { name = $"Cliente {Guid.NewGuid():N}", contactName = "Contacto" });

    private static async Task<Guid> CreateClientAsync(HttpClient http)
    {
        using var response = await PostClientAsync(http);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostInvoiceAsync(HttpClient http, Guid clientId)
        => http.PostAsJsonAsync(InvoicesEndpoint, new
        {
            clientId,
            dueAt = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            items = new[] { new { description = "Servicio", quantity = 1m, unitPrice = 100_000m } },
            currency = "COP",
        });

    private static Task<HttpResponseMessage> PostOrganizationAsync(HttpClient http, string legalType)
        => http.PostAsJsonAsync(OrganizationsEndpoint, new { legalType, name = $"Propia {Guid.NewGuid():N}", country = "CO" });

    private static async Task<Limits> PlanLimitsAsync(HttpClient http)
    {
        using var response = await http.GetAsync(PlanLimitsEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Limits>())!;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertQuotaExceededAsync(HttpResponseMessage response, string featureKey)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("QUOTA_EXCEEDED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(featureKey, body.RootElement.GetProperty("featureKey").GetString());
    }

    private async Task<string> PlanOfOwnerAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPlanResolutionService>().ResolveForOwnerAsync(ownerId);
    }

    // Organizaciones propias vigentes (el filtro global deja fuera las eliminadas), como las cuenta max_owned_orgs.
    private async Task<int> OwnedCountAsync(Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Organizations.CountAsync(o => o.OwnerId == ownerId);
    }

    private async Task<int> ClientCountAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Clients.IgnoreQueryFilters().CountAsync(c => c.OrganizationId == organizationId);
    }

    private async Task<Guid> FirstClientIdAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Clients.IgnoreQueryFilters().Where(c => c.OrganizationId == organizationId).Select(c => c.Id).FirstAsync();
    }

    // Mismo criterio que InvoiceRepository.CountThisMonthAsync (CreatedAt en el mes UTC en curso).
    private async Task<int> ActiveMembersAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationMembers.IgnoreQueryFilters()
            .CountAsync(m => m.OrganizationId == organizationId && m.DeletedAt == null);
    }

    private async Task<int> InvoicesThisMonthAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var now = DateTime.UtcNow;
        return await db.Invoices.IgnoreQueryFilters().CountAsync(i =>
            i.OrganizationId == organizationId && i.CreatedAt.Year == now.Year && i.CreatedAt.Month == now.Month);
    }
}

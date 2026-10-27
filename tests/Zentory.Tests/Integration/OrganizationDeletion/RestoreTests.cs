using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.API.Authorization;
using Zentory.API.Middleware;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Security.Idor;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 T11c sobre el host real: listado de organizaciones eliminadas y restauración (RF-72 a-d, RF-74 a; SR-0018.25,
// SR-0018.27, SR-0018.35; D-30, D-45). CA-104 completo; CA-T11C-1 (parte de CA-105) y CA-T11C-2 (parte de CA-106).
public sealed class RestoreTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Correcta-Restaurar-2026!";
    private const int    PurgeAfterDays = 30;
    private const string RestoreRoute   = "api/v1/organizations/{id:guid}/restore";
    private const string DeletedRoute   = "api/v1/me/organizations/deleted";

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    // Se ejecuta una vez justo antes de la escritura de la restauración, después de leer y contar (carreras de RF-78 y
    // D-45): otra operación confirma en su propio contexto y la restauración escribe con los sellos ya viejos.
    private Func<Task>? _afterRestorableRead;

    public RestoreTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<DeletedOrganizationReader>();
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp =>
                new HookedUnitOfWork(new UnitOfWork(sp.GetRequiredService<ZentoryDbContext>()), TakeHook));
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── CA-104: listado y restauración tal como estaba ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA104_ListadoDelOwner_IncluyeAEliminadaHace10DiasConPurgeAtIgualADeletedAtMas30Dias()
    {
        var tenants = await SeedAsync();
        var a = tenants.A;
        var deletedAt = DateTime.UtcNow.AddDays(-10);
        await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, a.OrganizationId, a.Owner.UserId, deletedAt);
        using var http = ClientFor(a.Owner, a.Organization, a.Plan);

        using var response = await http.GetAsync(OrganizationRestoreApi.DeletedEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(a.OrganizationId, item.GetProperty("id").GetGuid());
        Assert.Equal(a.Organization.Name, item.GetProperty("name").GetString());
        var listedDeletedAt = item.GetProperty("deletedAt").GetDateTime().ToUniversalTime();
        Assert.Equal(deletedAt, listedDeletedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(listedDeletedAt.AddDays(PurgeAfterDays), item.GetProperty("purgeAt").GetDateTime().ToUniversalTime());
        Assert.Equal(["id", "name", "deletedAt", "purgeAt"], item.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task PLAN0018_CA104_Restore_Responde204DeletedAtNullYFilasMiembrosYArchivosIgualesALaInstantanea()
    {
        var tenants = await SeedAsync();
        var a = tenants.A;
        var client = Client.Create(a.OrganizationId, $"Cliente {Guid.NewGuid():N}", "Contacto");
        await tenants.SeedAsync(client);
        var storage = _host.Services.GetRequiredService<FakeStorageService>();
        var fileKey = $"{a.OrganizationId}/projects/{Guid.NewGuid():N}/contrato.pdf";
        await storage.UploadAsync(fileKey, new MemoryStream(Encoding.UTF8.GetBytes("contrato")), "application/pdf");

        var countsBefore = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        var membersBefore = await MembersAsync(a.OrganizationId);
        var filesBefore = FilesOf(storage, a.OrganizationId);

        await DeleteThroughApiAsync(a);
        using var http = ClientFor(a.Owner, a.Organization, a.Plan);
        using var response = await OrganizationRestoreApi.RestoreAsync(http, a.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var restored = await OrganizationDeletionApi.OrganizationAsync(_host.Services, a.OrganizationId);
        Assert.Null(restored.DeletedAt);
        Assert.Null(restored.DeletedBy);
        Assert.Null(restored.PurgeStartedAt);
        Assert.Equal(a.Owner.UserId, restored.OwnerId);
        Assert.Equal(countsBefore, await OrganizationDeletionApi.RowCountsAsync(_host.Services));
        Assert.Equal(membersBefore, await MembersAsync(a.OrganizationId));
        Assert.Equal(filesBefore, FilesOf(storage, a.OrganizationId));
        Assert.True(await AnyAuditAsync(a.OrganizationId, RestoreOrganizationCommandHandler.AuditAction));
        Assert.True(await AnyActivityAsync(a.OrganizationId, RestoreOrganizationCommandHandler.ActivityAction));

        // Ya no figura entre las eliminadas.
        using var list = await http.GetAsync(OrganizationRestoreApi.DeletedEndpoint);
        Assert.Equal("[]", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PLAN0018_CA104_TrasRestaurar_ElMemberVuelveAVerAYSusPeticionesConActiveOrgA_Responden2xx()
    {
        var tenants = await SeedAsync();
        var a = tenants.A;
        var member = a.Member(OrgRole.Member);
        using var memberHttp = ClientFor(member, a.Organization, a.Plan);

        await DeleteThroughApiAsync(a);
        using (var during = await memberHttp.GetAsync("/api/v1/clients"))
            Assert.Equal(HttpStatusCode.Unauthorized, during.StatusCode);
        Assert.DoesNotContain(a.OrganizationId, await MyOrganizationsAsync(memberHttp));

        using var ownerHttp = ClientFor(a.Owner, a.Organization, a.Plan);
        using (var restore = await OrganizationRestoreApi.RestoreAsync(ownerHttp, a.OrganizationId))
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);

        Assert.Contains(a.OrganizationId, await MyOrganizationsAsync(memberHttp));
        using var clients = await memberHttp.GetAsync("/api/v1/clients");
        Assert.Equal(HttpStatusCode.OK, clients.StatusCode);
        using var profile = await memberHttp.GetAsync("/api/v1/organization/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    // ── CA-T11C-1 (parte de CA-105): max_owned_orgs con el plan propio ──────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA105_OwnerFreeConBActivaYAEliminada_Restore422QuotaExceededYTrasEliminarB204()
    {
        // B es la organización activa del owner free (tenants.A en el builder); A, otra propia eliminada en plazo.
        var tenants = await SeedAsync(ownerPlan: Plan.Free);
        var active = tenants.A;
        var deleted = await SeedOwnedOrganizationAsync(active.Owner, active.LegalType, DateTime.UtcNow.AddDays(-3));
        using var http = ClientFor(active.Owner, active.Organization, active.Plan);

        using (var blocked = await OrganizationRestoreApi.RestoreAsync(http, deleted.OrganizationId))
            await AssertRestoreQuotaExceededAsync(blocked);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(_host.Services, deleted.OrganizationId)).DeletedAt);

        await DeleteThroughApiAsync(active);
        using var allowed = await OrganizationRestoreApi.RestoreAsync(http, deleted.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, deleted.OrganizationId)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA105_OwnerFreeDesdeSesionConAgenciaStudioDeOtroOwnerActiva_RestoreSigueRespondiendo422()
    {
        // El plan de la organización activa (studio, ilimitado) no cuenta: se evalúa el plan propio (D-30, C-9).
        var tenants = await SeedAsync(ownerPlan: Plan.Free);
        var owner = tenants.A.Owner;
        var agency = tenants.B;
        Assert.Equal(Plan.Studio, agency.Plan);
        await tenants.SeedAsync(OrganizationMember.Create(agency.OrganizationId, owner.UserId, OrgRole.Member));
        var deleted = await SeedOwnedOrganizationAsync(owner, tenants.A.LegalType, DateTime.UtcNow.AddDays(-1));
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Studio, agency.LegalType, null);
        using var http = ClientFor(owner, agency.Organization, agency.Plan);

        using var response = await OrganizationRestoreApi.RestoreAsync(http, deleted.OrganizationId);

        await AssertRestoreQuotaExceededAsync(response);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(_host.Services, deleted.OrganizationId)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA105_RestauracionConcurrenteDelMismoOwner_LaQuePierdeVuelveAContarYResponde422()
    {
        // D-45: dos restauraciones del mismo owner free sin organizaciones vigentes; la otra gana entre la lectura y la
        // escritura (OwnershipStamp), así que esta vuelve a contar una vez y ya no cabe.
        var tenants = await SeedAsync(ownerPlan: Plan.Free);
        var owner = tenants.A.Owner;
        await DeleteThroughApiAsync(tenants.A);
        var first = await SeedOwnedOrganizationAsync(owner, tenants.A.LegalType, DateTime.UtcNow.AddDays(-2));
        var second = await SeedOwnedOrganizationAsync(owner, tenants.A.LegalType, DateTime.UtcNow.AddDays(-2));
        _afterRestorableRead = () => OrganizationRestoreApi.RestoreDirectlyAsync(_host.Services, second.OrganizationId, owner.UserId);
        using var http = ClientWithoutOrganization(owner);

        using var response = await OrganizationRestoreApi.RestoreAsync(http, first.OrganizationId);

        await AssertRestoreQuotaExceededAsync(response);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(_host.Services, first.OrganizationId)).DeletedAt);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, second.OrganizationId)).DeletedAt);
    }

    // ── CA-T11C-2 (parte de CA-106): 404 uniforme, sesión y listado ─────────────────────────────────────────────

    [Fact]
    [IdorCase("POST", RestoreRoute)]
    public async Task PLAN0018_CA106_Restore_CasosNoRestaurables_Responden404ConElMismoCuerpoYSinCambios()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithPlan(Plan.Free).WithMember(OrgRole.Admin).WithMember(OrgRole.Member))
            .ForB(b => b.WithPlan(Plan.Free))
            .BuildAsync();
        var a = tenants.A;
        var b = tenants.B;
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Free, a.LegalType, 1);

        // Del owner de A: una vigente (A), una fuera de plazo y una con la purga iniciada.
        var expired = await SeedOwnedOrganizationAsync(a.Owner, a.LegalType, DateTime.UtcNow.AddDays(-PurgeAfterDays - 1));
        var purging = await SeedOwnedOrganizationAsync(a.Owner, a.LegalType, DateTime.UtcNow.AddDays(-2));
        await OrganizationRestoreApi.MarkPurgeStartedAsync(_host.Services, purging.OrganizationId);
        // De otro owner (el de B, también en free), eliminada en plazo: el owner de A está en su límite y aun así es 404.
        var foreign = await SeedOwnedOrganizationAsync(b.Owner, b.LegalType, DateTime.UtcNow.AddDays(-2));
        // A eliminada en plazo, vista por su admin y su member.
        var deletedA = await SeedOwnedOrganizationAsync(a.Owner, a.LegalType, DateTime.UtcNow.AddDays(-2),
            members: [(a.Member(OrgRole.Admin), OrgRole.Admin), (a.Member(OrgRole.Member), OrgRole.Member)]);

        var cases = new (User Caller, Guid Target, string Label)[]
        {
            (a.Owner,                  Guid.NewGuid(),           "inexistente"),
            (a.Owner,                  a.OrganizationId,         "no eliminada"),
            (a.Member(OrgRole.Admin),  deletedA.OrganizationId,  "admin de la eliminada"),
            (a.Member(OrgRole.Member), deletedA.OrganizationId,  "member de la eliminada"),
            (b.Owner,                  deletedA.OrganizationId,  "eliminada de otro owner"),
            (a.Owner,                  expired.OrganizationId,   "fuera de plazo"),
            (a.Owner,                  purging.OrganizationId,   "purga iniciada"),
            (a.Owner,                  foreign.OrganizationId,   "de otro owner, con el que llama en su límite"),
        };

        var before = await OrganizationStatesAsync();
        var bodies = new List<string>();
        foreach (var (caller, target, label) in cases)
        {
            using var http = ClientFor(caller, a.Organization, a.Plan);
            using var response = await OrganizationRestoreApi.RestoreAsync(http, target);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{label}: {(int)response.StatusCode}");
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.DoesNotContain("QUOTA_EXCEEDED", bodies[0], StringComparison.Ordinal);
        Assert.Equal(before, await OrganizationStatesAsync());

        // Control positivo: el owner sí restaura la suya en plazo (con A eliminada para que quepa en free).
        await DeleteThroughApiAsync(a, password: null);
        using var ownerHttp = ClientFor(a.Owner, a.Organization, a.Plan);
        using var own = await OrganizationRestoreApi.RestoreAsync(ownerHttp, deletedA.OrganizationId);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA106_Restore_PierdeLaCarreraConElInicioDeLaPurga_Responde404SinRestaurar()
    {
        var tenants = await SeedAsync();
        var a = tenants.A;
        await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, a.OrganizationId, a.Owner.UserId, DateTime.UtcNow.AddDays(-5));
        _afterRestorableRead = () => OrganizationRestoreApi.MarkPurgeStartedAsync(_host.Services, a.OrganizationId);
        using var http = ClientFor(a.Owner, a.Organization, a.Plan);

        using var response = await OrganizationRestoreApi.RestoreAsync(http, a.OrganizationId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, a.OrganizationId);
        Assert.NotNull(organization.DeletedAt);
        Assert.NotNull(organization.PurgeStartedAt);
    }

    [Fact]
    public async Task PLAN0018_CA106_OwnerRestauraDesdeUnaSesionCuyaOrganizacionActivaEsOtra_Responde204()
    {
        var tenants = await SeedAsync();
        var owner = tenants.A.Owner;
        var deleted = await SeedOwnedOrganizationAsync(owner, tenants.A.LegalType, DateTime.UtcNow.AddDays(-4));
        await tenants.SeedAsync(OrganizationMember.Create(tenants.B.OrganizationId, owner.UserId, OrgRole.Member));
        using var http = ClientFor(owner, tenants.B.Organization, tenants.B.Plan);

        using var response = await OrganizationRestoreApi.RestoreAsync(http, deleted.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, deleted.OrganizationId)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA106_OwnerSinOrganizacionActiva_ListaSusEliminadasYRestaura()
    {
        // RF-85 a: la sesión sin active_org_id sirve en el listado y en restore (las rutas no exigen organización).
        var tenants = await SeedAsync(ownerPlan: Plan.Free);
        var a = tenants.A;
        await DeleteThroughApiAsync(a);
        using var http = ClientWithoutOrganization(a.Owner);

        using (var list = await http.GetAsync(OrganizationRestoreApi.DeletedEndpoint))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Equal([a.OrganizationId], body.RootElement.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        }
        // Control: fuera de la lista cerrada, la misma sesión no entra.
        using (var other = await http.GetAsync("/api/v1/clients"))
            Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);

        using var restore = await OrganizationRestoreApi.RestoreAsync(http, a.OrganizationId);
        Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(_host.Services, a.OrganizationId)).DeletedAt);
    }

    [Fact]
    [IdorCase("GET", DeletedRoute)]
    public async Task PLAN0018_CA106_ListadoDeEliminadas_DeUnAdminOMemberDeA_NoListaAYElOwnerSi()
    {
        var tenants = await SeedAsync();
        var a = tenants.A;
        await DeleteThroughApiAsync(a);
        await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, tenants.B.OrganizationId, tenants.B.Owner.UserId,
            DateTime.UtcNow.AddDays(-1));

        foreach (var user in new[] { a.Member(OrgRole.Admin), a.Member(OrgRole.Member) })
        {
            using var http = ClientFor(user, a.Organization, a.Plan);
            using var response = await http.GetAsync(OrganizationRestoreApi.DeletedEndpoint);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        }

        // El owner ve A y no la eliminada de otro owner (B).
        using var ownerHttp = ClientFor(a.Owner, a.Organization, a.Plan);
        using var ownerList = await ownerHttp.GetAsync(OrganizationRestoreApi.DeletedEndpoint);
        using var body = JsonDocument.Parse(await ownerList.Content.ReadAsStringAsync());
        Assert.Equal([a.OrganizationId], body.RootElement.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task PLAN0018_CA106_ListadoDeEliminadas_ExcluyeFueraDePlazoYConPurgaIniciada()
    {
        var tenants = await SeedAsync();
        var owner = tenants.A.Owner;
        var legalType = tenants.A.LegalType;
        var inWindow = await SeedOwnedOrganizationAsync(owner, legalType, DateTime.UtcNow.AddDays(-PurgeAfterDays + 1));
        await SeedOwnedOrganizationAsync(owner, legalType, DateTime.UtcNow.AddDays(-PurgeAfterDays).AddMinutes(-1));
        var purging = await SeedOwnedOrganizationAsync(owner, legalType, DateTime.UtcNow.AddDays(-1));
        await OrganizationRestoreApi.MarkPurgeStartedAsync(_host.Services, purging.OrganizationId);
        using var http = ClientFor(owner, tenants.A.Organization, tenants.A.Plan);

        using var response = await http.GetAsync(OrganizationRestoreApi.DeletedEndpoint);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal([inWindow.OrganizationId], body.RootElement.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        using var scope = _host.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<DeletedOrganizationReader>();
        Assert.True(await reader.HasRestorableAsync(owner.UserId));
        Assert.False(await reader.HasRestorableAsync(tenants.B.Owner.UserId));
    }

    [Fact]
    public void PLAN0018_CA106_Restore_LlevaElLimiteOrganizationRestoreYNoExigeReautenticacion()
    {
        var method = typeof(Zentory.API.Controllers.V1.OrganizationsController).GetMethod("Restore")!;
        var limits = method.GetCustomAttributes(typeof(AuthRateLimitAttribute), inherit: true)
            .Cast<AuthRateLimitAttribute>().Select(l => l.Policy).ToList();

        Assert.Equal([AuthRateLimitPolicies.OrganizationRestore], limits);
        Assert.NotNull(method.GetCustomAttributes(typeof(SkipMembershipValidationAttribute), inherit: true).SingleOrDefault());
        var policy = AuthRateLimitPolicies.Defaults.Single(p => p.Name == AuthRateLimitPolicies.OrganizationRestore);
        var rule = Assert.Single(policy.Rules);
        Assert.Equal((5, TimeSpan.FromHours(1), AuthRateLimitPartition.User), (rule.PermitLimit, rule.Window, rule.Partition));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<TenantPair> SeedAsync(string ownerPlan = Plan.Studio)
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithPlan(ownerPlan).WithMember(OrgRole.Admin).WithMember(OrgRole.Member).WithOwnerPassword(OwnerPassword))
            .BuildAsync();
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Free, tenants.A.LegalType, 1);
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Studio, tenants.A.LegalType, null);
        return tenants;
    }

    // Otra organización del mismo owner, eliminada en deletedAtUtc, con los miembros indicados además del owner.
    private async Task<OrganizationEntity> SeedOwnedOrganizationAsync(
        User owner, string legalType, DateTime deletedAtUtc, IReadOnlyList<(User User, string Role)>? members = null)
    {
        var organization = OrganizationEntity.Create($"Propia {Guid.NewGuid():N}", legalType);
        organization.SetOwner(owner.UserId);
        organization.MarkDeleted(owner.UserId, deletedAtUtc);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, owner.UserId, OrgRole.Owner));
        foreach (var (user, role) in members ?? [])
            db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, user.UserId, role, invitedBy: owner.UserId));
        await db.SaveChangesAsync();
        return organization;
    }

    private async Task DeleteThroughApiAsync(SeededTenant tenant, string? password = OwnerPassword)
    {
        if (password is null)
        {
            await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, tenant.OrganizationId, tenant.Owner.UserId, DateTime.UtcNow);
            return;
        }
        using var http = ClientFor(tenant.Owner, tenant.Organization, tenant.Plan);
        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenant.Organization.Name, password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // JWT real de usuario con la organización activa indicada; el rol lo lee el middleware de la base.
    private HttpClient ClientFor(User user, OrganizationEntity activeOrganization, string plan)
    {
        var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Tokens.ForUser(user, activeOrganization, OrgRole.Member, plan));
        return http;
    }

    private HttpClient ClientWithoutOrganization(User user)
    {
        var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", OrganizationRestoreApi.TokenWithoutOrganization(_host.Services, user));
        return http;
    }

    private static async Task AssertRestoreQuotaExceededAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("QUOTA_EXCEEDED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(PlanLimits.FeatureKeys.MaxOwnedOrgs, body.RootElement.GetProperty("featureKey").GetString());
        var message = body.RootElement.GetProperty("message").GetString();
        Assert.Equal(RestoreQuotaExceededException.RestoreMessage, message);
        Assert.Contains("elimina otra organización o sube de plan", message, StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<Guid>> MyOrganizationsAsync(HttpClient http)
    {
        using var response = await http.GetAsync("/api/v1/me/organizations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(o => Guid.Parse(o.GetProperty("orgId").GetString()!)).ToList();
    }

    private async Task<List<string>> MembersAsync(Guid organizationId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.OrganizationMembers.IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.UserId)
            .Select(m => $"{m.UserId}:{m.Role}:{m.DeletedAt}")
            .ToListAsync();
    }

    private static List<string> FilesOf(FakeStorageService storage, Guid organizationId)
        => storage.Objects.Keys.Where(k => k.StartsWith($"{organizationId}/", StringComparison.Ordinal)).Order().ToList();

    // Estado de todas las organizaciones (también las eliminadas): id, borrado, purga y sello.
    private async Task<List<string>> OrganizationStatesAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(o => o.OrganizationId)
            .Select(o => $"{o.OrganizationId}:{o.DeletedAt}:{o.PurgeStartedAt}:{o.ConcurrencyStamp}")
            .ToListAsync();
    }

    private async Task<bool> AnyAuditAsync(Guid organizationId, string action)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.AuditLogs.IgnoreQueryFilters().AnyAsync(l => l.RecordId == organizationId && l.Action == action);
    }

    private async Task<bool> AnyActivityAsync(Guid organizationId, string action)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.ActivityLogs.IgnoreQueryFilters().AnyAsync(l => l.OrganizationId == organizationId && l.Action == action);
    }

    // El decorador HookedUnitOfWork (Support) corre una vez la operación que compite con la restauración.
    private Func<Task>? TakeHook() => Interlocked.Exchange(ref _afterRestorableRead, null);
}

// PLAN-0018 CA-T11C-2 (parte de CA-106; SR-0018.29) con los límites organization-delete y organization-restore en sus
// valores por defecto: el 6.º intento de la hora responde 429 sin efecto, y el contador es por usuario.
public sealed class RestoreRateLimitTests(OrganizationRateLimitApiFactory factory) : IClassFixture<OrganizationRateLimitApiFactory>
{
    private const string OwnerPassword = "Owner-Correcta-Limite-Restaurar-2026!";
    private const int    Attempts      = 5;

    [Fact]
    public async Task PLAN0018_CA106_Restore_SuperaCincoPorHoraPorUsuario_ElSextoResponde429SinRestaurar()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(factory.Services, Plan.Studio, tenants.A.LegalType, null);
        var a = tenants.A;
        await OrganizationRestoreApi.MarkDeletedAsync(factory.Services, a.OrganizationId, a.Owner.UserId, DateTime.UtcNow.AddDays(-1));
        using var http = tenants.ClientFor(a);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var missing = await OrganizationRestoreApi.RestoreAsync(http, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var limited = await OrganizationRestoreApi.RestoreAsync(http, a.OrganizationId))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Contains(AuthRateLimitAttribute.RateLimitedCode, await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.True(limited.Headers.Contains("Retry-After"));
        }
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(factory.Services, a.OrganizationId)).DeletedAt);

        // El contador es por usuario: el owner de B restaura la suya.
        await OrganizationRestoreApi.MarkDeletedAsync(factory.Services, tenants.B.OrganizationId, tenants.B.Owner.UserId, DateTime.UtcNow);
        using var other = tenants.ClientFor(tenants.B);
        using var allowed = await OrganizationRestoreApi.RestoreAsync(other, tenants.B.OrganizationId);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA106_DeleteOrganization_SuperaCincoPorHoraPorUsuario_ElSextoResponde429SinEliminar()
    {
        var tenants = await new TenantBuilder(factory).ForA(a => a.WithOwnerPassword(OwnerPassword)).BuildAsync();
        var a = tenants.A;
        using var http = tenants.ClientFor(a);

        // Nombre distinto: 422 sin efecto, pero el intento cuenta para organization-delete.
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var rejected = await OrganizationDeletionApi.DeleteAsync(http, "otro nombre", OwnerPassword);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        }

        using var limited = await OrganizationDeletionApi.DeleteAsync(http, a.Organization.Name, OwnerPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains(AuthRateLimitAttribute.RateLimitedCode, await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(factory.Services, a.OrganizationId)).DeletedAt);
    }
}

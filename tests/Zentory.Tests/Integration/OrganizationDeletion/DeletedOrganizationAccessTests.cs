using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zentory.API.Extensions;
using Zentory.API.Middleware;
using Zentory.Application.CollaboratorPortal;
using Zentory.Application.Common.Options;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T11-1 (parte de CA-103; RF-65 c, d, f, g; RF-14; D-31; D-39) sobre el host real: tras eliminar A con
// DELETE /organization, las sesiones de usuario y de colaborador en A reciben 401 ORGANIZATION_DELETED en la siguiente
// petición; GET /me/organizations y el cambio del portal ya no la ofrecen; salen los correos a admin, member y owner (este
// con la fecha límite de restauración); y A deja de contar para max_owned_orgs.
public sealed class DeletedOrganizationAccessTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword    = "Owner-Correcta-Acceso-2026!";
    private const string CollaboratorType = "hourly_contractor";
    private const int    PurgeAfterDays   = 12;
    private const string DeletedCode      = MembershipValidationMiddleware.OrganizationDeletedCode;

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    public DeletedOrganizationAccessTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        // Un plazo distinto del de por defecto: la fecha del correo sale de la configuración.
        _host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(OrganizationsOptions.PurgeAfterDaysKey, PurgeAfterDays.ToString()));
    }

    public void Dispose() => _host.Dispose();

    // ── Sesiones de usuario ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v1/organization/profile")]
    [InlineData("/api/v1/clients")]
    [InlineData("/API/V1/ORGANIZATION/SETTINGS")]
    public async Task PLAN0018_CA103_TrasEliminarA_OwnerAdminYMemberConActiveOrgA_Reciben401OrganizationDeleted(string path)
    {
        var scenario = await SeedAsync();
        var clients = scenario.UsersOfA.Select(user => ClientFor(user, scenario.Tenants.A)).ToList();
        foreach (var http in clients)
            using (var before = await http.GetAsync(path))
                Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await DeleteAAsync(scenario);

        foreach (var http in clients)
        {
            using var after = await http.GetAsync(path);
            await AssertUnauthorizedAsync(after, DeletedCode);
            http.Dispose();
        }
    }

    [Fact]
    public async Task PLAN0018_CA103_TrasEliminarA_GetMeOrganizations_NoContieneAPeroSiSusOtrasOrganizaciones()
    {
        var scenario = await SeedAsync();
        await DeleteAAsync(scenario);

        foreach (var user in scenario.UsersOfA)
        {
            // Con el token de A: la ruta está excluida de la revalidación y es la que usa el frontend para cambiar (D-32).
            using var http = ClientFor(user, scenario.Tenants.A);
            using var response = await http.GetAsync("/api/v1/me/organizations");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var ids = body.RootElement.EnumerateArray().Select(o => o.GetProperty("orgId").GetString()).ToList();
            Assert.DoesNotContain(scenario.Tenants.A.OrganizationId.ToString(), ids);
            Assert.Contains(scenario.Tenants.B.OrganizationId.ToString(), ids);
        }
    }

    [Fact]
    public async Task PLAN0018_CA103_TrasEliminarA_MembresiaYaRevocada_Recibe401MembershipRevokedYNoOrganizationDeleted()
    {
        // Sin membresía vigente no se revela el estado de la organización.
        var scenario = await SeedAsync();
        var member = scenario.Tenants.A.Member(OrgRole.Member);
        using var http = ClientFor(member, scenario.Tenants.A);
        await WithDbAsync(async db =>
        {
            var membership = await db.OrganizationMembers.IgnoreQueryFilters()
                .SingleAsync(m => m.OrganizationId == scenario.Tenants.A.OrganizationId && m.UserId == member.UserId);
            membership.SoftDelete();
        });

        await DeleteAAsync(scenario);
        using var response = await http.GetAsync("/api/v1/organization/profile");

        await AssertUnauthorizedAsync(response, MembershipValidationMiddleware.MembershipRevokedCode);
    }

    // ── Portal de colaboradores ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/collaborator-portal/me")]
    [InlineData("GET", "/api/v1/collaborator-portal/pila")]
    [InlineData("GET", "/api/v1/collaborator-portal/payout-invoices")]
    [InlineData("POST", "/api/v1/collaborator-portal/switch")]
    public async Task PLAN0018_CA103_TrasEliminarA_SesionDeColaboradorEnAConJwtVigente_Recibe401OrganizationDeleted(
        string method, string path)
    {
        var scenario = await SeedAsync();
        var session = CollaboratorSession(scenario, scenario.CollaboratorInA, scenario.Tenants.A.Organization);

        // Control: antes de eliminar, el mismo JWT entra al portal.
        using (var before = await SendAsCollaboratorAsync(HttpMethod.Get, "/api/v1/collaborator-portal/me", session))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await DeleteAAsync(scenario);
        using var after = await SendAsCollaboratorAsync(new HttpMethod(method), path, session,
            method == "POST" ? new { collaboratorId = scenario.CollaboratorInB.Id } : null);

        await AssertUnauthorizedAsync(after, DeletedCode);
    }

    [Fact]
    public async Task PLAN0018_CA103_TrasEliminarA_SwitchDelPortalDesdeB_YaNoOfreceA()
    {
        var scenario = await SeedAsync();
        var sessionB = CollaboratorSession(scenario, scenario.CollaboratorInB, scenario.Tenants.B.Organization);

        await DeleteAAsync(scenario);

        using (var toA = await SendAsCollaboratorAsync(HttpMethod.Post, "/api/v1/collaborator-portal/switch", sessionB,
                   new { collaboratorId = scenario.CollaboratorInA.Id }))
            Assert.Equal(HttpStatusCode.NotFound, toA.StatusCode);

        using var toB = await SendAsCollaboratorAsync(HttpMethod.Post, "/api/v1/collaborator-portal/switch", sessionB,
            new { collaboratorId = scenario.CollaboratorInB.Id });
        Assert.Equal(HttpStatusCode.OK, toB.StatusCode);
        using var body = JsonDocument.Parse(await toB.Content.ReadAsStringAsync());
        var offered = body.RootElement.GetProperty("memberships").EnumerateArray()
            .Select(m => m.GetProperty("collaboratorId").GetGuid()).ToList();
        Assert.Equal([scenario.CollaboratorInB.Id], offered);
    }

    // ── Correos (RF-65 f; D-39) ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_TrasEliminarA_CorreoAlAdminAlMemberYAlOwnerConLaFechaLimiteDeRestauracion()
    {
        var scenario = await SeedAsync();
        var a = scenario.Tenants.A;
        var emails = _host.Services.GetRequiredService<FakeEmailService>();

        await DeleteAAsync(scenario);

        var recipients = scenario.UsersOfA.Select(u => u.Email).ToHashSet();
        await OrganizationDeletionApi.WaitUntilAsync(
            () => emails.Sent.Count(e => recipients.Contains(e.ToEmail)) >= 3, "La cola no entregó los tres avisos a tiempo.");
        var sent = emails.Sent.Where(e => recipients.Contains(e.ToEmail)).ToList();
        var deletedAt = (await OrganizationDeletionApi.OrganizationAsync(_host.Services, a.OrganizationId)).DeletedAt!.Value;

        Assert.Equal(3, sent.Count);
        foreach (var role in new[] { OrgRole.Admin, OrgRole.Member })
        {
            var notice = Assert.Single(sent, e => e.ToEmail == a.Member(role).Email);
            Assert.Equal(nameof(FakeEmailService.SendOrganizationDeletedNoticeAsync), notice.Kind);
            Assert.Equal(a.Organization.Name, notice.OrganizationName);
            Assert.Null(notice.RestoreDeadlineUtc);
        }
        var owner = Assert.Single(sent, e => e.ToEmail == a.Owner.Email);
        Assert.Equal(nameof(FakeEmailService.SendOrganizationDeletedOwnerNoticeAsync), owner.Kind);
        Assert.Equal(a.Organization.Name, owner.OrganizationName);
        Assert.Equal(deletedAt.AddDays(PurgeAfterDays), owner.RestoreDeadlineUtc);
    }

    [Fact]
    public async Task PLAN0018_CA103_CorreosDeEliminacion_CodificanLosValoresYNoLlevanEnlacesNiTokens()
    {
        // Plantillas reales de ResendEmailService (SR-0018.22): el nombre de la organización y del destinatario se codifican.
        var resend = CapturingResend.Create();
        var service = new ResendEmailService(resend, new ConfigurationBuilder().Build());
        const string hostileOrg  = "<img src=x onerror=alert(1)> & Cía";
        const string hostileName = "<script>alert(2)</script>";
        var deadline = new DateTime(2026, 11, 24, 15, 30, 0, DateTimeKind.Utc);

        await service.SendOrganizationDeletedNoticeAsync("member@example.test", hostileName, hostileOrg);
        await service.SendOrganizationDeletedOwnerNoticeAsync("owner@example.test", hostileName, hostileOrg, deadline);

        var messages = CapturingResend.Of(resend).Messages;
        Assert.Equal(2, messages.Count);
        foreach (var message in messages)
        {
            Assert.Contains("&lt;img src=x onerror=alert(1)&gt; &amp; C&#237;a", message.HtmlBody);
            Assert.Contains("&lt;script&gt;alert(2)&lt;/script&gt;", message.HtmlBody);
            Assert.DoesNotContain("<script>", message.HtmlBody);
            Assert.DoesNotContain("<img", message.HtmlBody);
            Assert.DoesNotContain("href", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(hostileOrg, message.Subject);
        }
        Assert.Contains("24/11/2026 15:30", messages[1].HtmlBody);
        Assert.DoesNotContain("24/11/2026", messages[0].HtmlBody);
    }

    // ── Límite de organizaciones propias (RF-65 g) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_TrasEliminarA_ElOwnerPuedeCrearOtraOrganizacionPropiaEnFree()
    {
        var scenario = await SeedAsync();
        await SeedOwnedOrganizationLimitAsync(scenario.Tenants.A.LegalType, limit: 1);
        using var http = ClientFor(scenario.Tenants.A.Owner, scenario.Tenants.A);
        var request = new { legalType = scenario.Tenants.A.LegalType, name = $"Nueva {Guid.NewGuid():N}", country = "CO" };

        // Control: con A vigente, free ya está en su límite de una organización propia.
        using (var before = await http.PostAsJsonAsync("/api/v1/organizations", request))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, before.StatusCode);
            Assert.Contains("QUOTA_EXCEEDED", await before.Content.ReadAsStringAsync());
        }

        await DeleteAAsync(scenario);
        using var after = await http.PostAsJsonAsync("/api/v1/organizations", request);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    // ── Configuración (Organizations:PurgeAfterDays) ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task PLAN0018_CA103_Host_ConPurgeAfterDaysMenorQueUno_NoArranca(string value)
    {
        await using var factory = new MinimalHostFactory(AppEnvironments.Testing, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]          = "true",
            [DatabaseSettings.InMemoryNameKey]         = $"zentory-purge-days-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]        = "false",
            [DevSeedSettings.EnabledKey]               = "false",
            [OrganizationsOptions.PurgeAfterDaysKey]   = value,
        });

        var error = Record.Exception(() => factory.CreateClient());

        Assert.True(Mentions(error, OrganizationsOptions.PurgeAfterDaysKey), $"El arranque no falló por el plazo: {error?.GetType().Name}");
    }

    [Fact]
    public void PLAN0018_CA103_OrganizationsOptions_PorDefectoTreintaDiasYElConfiguradoGana()
    {
        Assert.Equal(OrganizationsOptions.DefaultPurgeAfterDays,
            _factory.Services.GetRequiredService<IOptions<OrganizationsOptions>>().Value.PurgeAfterDays);
        Assert.Equal(30, OrganizationsOptions.DefaultPurgeAfterDays);
        Assert.Equal(PurgeAfterDays, _host.Services.GetRequiredService<IOptions<OrganizationsOptions>>().Value.PurgeAfterDays);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Scenario(
        TenantPair Tenants, IReadOnlyList<User> UsersOfA, string CollaboratorEmail, Collaborator CollaboratorInA,
        Collaborator CollaboratorInB);

    // A (free) con owner, admin y member, que también son miembros de B; y un colaborador con el mismo email en A y B.
    private async Task<Scenario> SeedAsync()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithPlan(Plan.Free).WithMember(OrgRole.Admin).WithMember(OrgRole.Member).WithOwnerPassword(OwnerPassword))
            .BuildAsync();
        var a = tenants.A;
        var usersOfA = new List<User> { a.Owner, a.Member(OrgRole.Admin), a.Member(OrgRole.Member) };

        var email = $"colaborador-{Guid.NewGuid():N}@zentory.test";
        var inA = Collaborator.Create(a.OrganizationId, "Colaborador A", CollaboratorType, email: email);
        var inB = Collaborator.Create(tenants.B.OrganizationId, "Colaborador B", CollaboratorType, email: email);

        await tenants.SeedAsync(
            usersOfA.Select(u => (object)OrganizationMember.Create(tenants.B.OrganizationId, u.UserId, OrgRole.Member))
                .Append(inA).Append(inB).ToArray());

        return new Scenario(tenants, usersOfA, email, inA, inB);
    }

    private async Task DeleteAAsync(Scenario scenario)
    {
        using var http = ClientFor(scenario.Tenants.A.Owner, scenario.Tenants.A);
        using var response = await OrganizationDeletionApi.DeleteAsync(http, scenario.Tenants.A.Organization.Name, OwnerPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // JWT real de usuario con la organización activa indicada; el rol del claim no cuenta (RF-14 lo lee de la base).
    private HttpClient ClientFor(User user, SeededTenant tenant)
    {
        var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Tokens.ForUser(user, tenant.Organization, OrgRole.Member, tenant.Plan));
        return http;
    }

    private string CollaboratorSession(Scenario scenario, Collaborator active, OrganizationEntity organization)
        => _factory.Tokens.ForCollaborator(
            scenario.CollaboratorEmail, [scenario.CollaboratorInA.Id, scenario.CollaboratorInB.Id], active.Id,
            organization.OrganizationId, organization.Name, CollaboratorAuthMethod.MagicLink);

    private async Task<HttpResponseMessage> SendAsCollaboratorAsync(HttpMethod method, string path, string token, object? body = null)
    {
        using var http = _host.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await http.SendAsync(request);
    }

    private static async Task AssertUnauthorizedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private async Task WithDbAsync(Func<ZentoryDbContext, Task> change)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        await change(db);
        await db.SaveChangesAsync();
    }

    // Límite max_owned_orgs del plan free para ese tipo de cuenta, una sola vez por base.
    private Task SeedOwnedOrganizationLimitAsync(string legalType, int limit) => WithDbAsync(async db =>
    {
        var plan = await db.BillingPlans.FirstAsync(p => p.Name == Plan.Free);
        var exists = await db.PlanLimits.AnyAsync(l =>
            l.PlanId == plan.Id && l.LegalType == legalType && l.FeatureKey == PlanLimits.FeatureKeys.MaxOwnedOrgs);
        if (!exists)
            db.PlanLimits.Add(PlanLimit.Create(plan.Id, legalType, PlanLimits.FeatureKeys.MaxOwnedOrgs, limit));
    });

    private static bool Mentions(Exception? exception, string key)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(key, StringComparison.Ordinal))
                return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(inner => Mentions(inner, key)))
                return true;
        }
        return false;
    }
}

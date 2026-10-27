using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.PilaVerifications.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-T11A-2 (parte de CA-103; RF-16 rev. 4, RF-70 rev. 4, RF-65 e; SR-0018.26 d; D-31) sobre el host real:
// tras eliminar A con DELETE /organization, ni switch, ni refresh, ni login, ni Google entregan A, y el job
// monthly-pila-auto-request no la procesa. La persona U es owner de A (con contraseña y Google) y member de B.
public sealed class DeletedOrganizationSessionTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string Password         = "Owner-Correcta-Sesion-2026!";
    private const string CollaboratorType = "hourly_contractor";
    private const string ActiveOrgClaim   = "active_org_id";

    private const string LoginEndpoint   = "/api/v1/auth/login";
    private const string GoogleEndpoint  = "/api/v1/auth/google";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string SwitchEndpoint  = "/api/v1/organization/switch";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeGoogleTokenValidator       _google = new();
    private readonly WebApplicationFactory<Program> _host;

    public DeletedOrganizationSessionTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(_google);
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── POST /organization/switch ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_SwitchHaciaOrganizacionEliminada_Responde404SinTokenYSinTocarLastActiveOrganizationId()
    {
        var scenario = await SeedAsync();
        using (var toB = await SwitchAsync(scenario, from: scenario.B, to: scenario.B.OrganizationId))
            Assert.Equal(HttpStatusCode.OK, toB.StatusCode);
        Assert.Equal(scenario.B.OrganizationId, await LastActiveAsync(scenario.User.UserId));

        await DeleteAAsync(scenario);

        using var deleted = await SwitchAsync(scenario, from: scenario.B, to: scenario.A.OrganizationId);
        using var missing = await SwitchAsync(scenario, from: scenario.B, to: Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        var body = await deleted.Content.ReadAsStringAsync();
        Assert.Equal(await missing.Content.ReadAsStringAsync(), body);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(scenario.B.OrganizationId, await LastActiveAsync(scenario.User.UserId));
    }

    // ── POST /auth/refresh ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_RefreshConActiveOrgIdDeOrganizacionEliminada_EmiteTokenDeOtraOrganizacionNuncaA()
    {
        var scenario = await SeedAsync();
        var control = await SeedRefreshTokenAsync(scenario.User);
        var refreshToken = await SeedRefreshTokenAsync(scenario.User);

        // Control: con A vigente, refresh respeta la organización pedida.
        using (var before = await RefreshAsync(control, scenario.A.OrganizationId))
            Assert.Equal(scenario.A.OrganizationId, await ActiveOrgOfAsync(before));

        await DeleteAAsync(scenario);

        using var after = await RefreshAsync(refreshToken, scenario.A.OrganizationId);
        Assert.Equal(scenario.B.OrganizationId, await ActiveOrgOfAsync(after));
        Assert.Equal([scenario.B.OrganizationId], await MembershipIdsAsync(after));
    }

    // ── POST /auth/login y POST /auth/google (IAuthSessionIssuer) ───────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_LoginConLastActiveOrganizationIdEliminada_EntraAOtraOrganizacion()
    {
        var scenario = await SeedWithLastActiveAAsync();

        // Control: con A vigente, el login entra a la última organización usada.
        using (var before = await LoginAsync(scenario.User))
            Assert.Equal(scenario.A.OrganizationId, await ActiveOrgOfAsync(before));

        await DeleteAAsync(scenario);
        Assert.Equal(scenario.A.OrganizationId, await LastActiveAsync(scenario.User.UserId));

        using var after = await LoginAsync(scenario.User);
        Assert.Equal(scenario.B.OrganizationId, await ActiveOrgOfAsync(after));
        Assert.Equal([scenario.B.OrganizationId], await MembershipIdsAsync(after));
    }

    [Fact]
    public async Task PLAN0018_CA103_GoogleConLastActiveOrganizationIdEliminada_EntraAOtraOrganizacion()
    {
        var scenario = await SeedWithLastActiveAAsync();

        using (var before = await GoogleLoginAsync(scenario.User))
            Assert.Equal(scenario.A.OrganizationId, await ActiveOrgOfAsync(before));

        await DeleteAAsync(scenario);

        using var after = await GoogleLoginAsync(scenario.User);
        Assert.Equal(scenario.B.OrganizationId, await ActiveOrgOfAsync(after));
        Assert.Equal([scenario.B.OrganizationId], await MembershipIdsAsync(after));
    }

    // ── Job monthly-pila-auto-request ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA103_JobMonthlyPilaAutoRequest_NoProcesaLaOrganizacionEliminada()
    {
        // A y B son empresa en studio, las dos con un colaborador elegible: solo cambia que A está eliminada.
        var scenario = await SeedAsync();
        var inA = NewCollaborator(scenario.A);
        var inB = NewCollaborator(scenario.B);
        await scenario.Tenants.SeedAsync(inA, inB);
        await DeleteAAsync(scenario);
        var emails = _host.Services.GetRequiredService<FakeEmailService>();

        using (var scope = _host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RunMonthlyPilaAutoRequestCommand());

        Assert.Equal(0, await PilaRequestsAsync(scenario.A.OrganizationId));
        Assert.Equal(1, await PilaRequestsAsync(scenario.B.OrganizationId));
        Assert.DoesNotContain(emails.Sent, e => e.ToEmail == inA.Email);
        Assert.Single(emails.Sent, e => e.ToEmail == inB.Email);
    }

    [Fact]
    public void PLAN0018_CA103_JobsRecurrentes_SoloMonthlyPilaAutoRequestYOrganizationPurge()
    {
        // MonthlyPilaAutoRequest hereda el filtro global de Organization y salta las eliminadas (D-31); OrganizationPurge
        // (T12b, RF-78) las busca a propósito, desde la lista blanca de IgnoreQueryFilters. Un job recurrente nuevo rompe
        // este test y obliga a decidir cómo salta las organizaciones eliminadas (RF-65 e).
        var ids = typeof(RecurringJobIds).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
        var schedules = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(file => CountOccurrences(File.ReadAllText(file), ".AddOrUpdate<"));

        Assert.Equal([RecurringJobIds.MonthlyPilaAutoRequest, RecurringJobIds.OrganizationPurge], ids);
        Assert.Equal(2, schedules);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Scenario(TenantPair Tenants)
    {
        public SeededTenant A    => Tenants.A;
        public SeededTenant B    => Tenants.B;
        public User         User => Tenants.A.Owner;
    }

    // U: owner de A con contraseña y Google; member de B (de otro owner). A no es la única organización de U.
    private async Task<Scenario> SeedAsync()
    {
        var tenants = await new TenantBuilder(_factory)
            .ForA(a => a.WithOwnerPassword(Password).WithOwnerGoogleId($"google-{Guid.NewGuid():N}"))
            .BuildAsync();
        await tenants.SeedAsync(OrganizationMember.Create(tenants.B.OrganizationId, tenants.A.Owner.UserId, OrgRole.Member));
        return new Scenario(tenants);
    }

    // Como SeedAsync, con LastActiveOrganizationId = A puesto por switch (el único que lo escribe, RF-70).
    private async Task<Scenario> SeedWithLastActiveAAsync()
    {
        var scenario = await SeedAsync();
        using (var toA = await SwitchAsync(scenario, from: scenario.B, to: scenario.A.OrganizationId))
            Assert.Equal(HttpStatusCode.OK, toA.StatusCode);
        Assert.Equal(scenario.A.OrganizationId, await LastActiveAsync(scenario.User.UserId));
        return scenario;
    }

    private static Collaborator NewCollaborator(SeededTenant tenant)
        => Collaborator.Create(tenant.OrganizationId, $"Colaborador {Guid.NewGuid():N}", CollaboratorType,
            email: $"pila-{Guid.NewGuid():N}@zentory.test");

    private async Task DeleteAAsync(Scenario scenario)
    {
        using var http = OrganizationDeletionApi.ClientFor(_host, scenario.Tenants, scenario.A);
        using var response = await OrganizationDeletionApi.DeleteAsync(http, scenario.A.Organization.Name, Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Token de U con la organización activa `from`; el rol del claim no cuenta (RF-14 lo lee de la base).
    private Task<HttpResponseMessage> SwitchAsync(Scenario scenario, SeededTenant from, Guid to)
        => SendAsync(SwitchEndpoint, _factory.Tokens.ForUser(scenario.User, from.Organization, OrgRole.Member, from.Plan),
            new { orgId = to });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken, Guid activeOrgId)
        => SendAsync(RefreshEndpoint, token: null, new { refreshToken, activeOrgId });

    private Task<HttpResponseMessage> LoginAsync(User user)
        => SendAsync(LoginEndpoint, token: null, new { email = user.Email, password = Password });

    private Task<HttpResponseMessage> GoogleLoginAsync(User user)
    {
        var idToken = _google.Register(new GoogleUserInfo(user.GoogleId!, user.Email, user.FirstName, user.LastName, EmailVerified: true));
        return SendAsync(GoogleEndpoint, token: null, new { idToken });
    }

    private async Task<HttpResponseMessage> SendAsync(string path, string? token, object body)
    {
        using var http = _host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private Task<string> SeedRefreshTokenAsync(User user) => AuthSessionApi.SeedRefreshTokenAsync(_host.Services, user);

    private async Task<Guid?> LastActiveAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.LastActiveOrganizationId)
            .SingleAsync();
    }

    private async Task<int> PilaRequestsAsync(Guid organizationId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.PilaVerifications.IgnoreQueryFilters().CountAsync(p => p.OrganizationId == organizationId);
    }

    // La organización del token emitido, que debe coincidir con la del perfil de la respuesta.
    private static async Task<Guid> ActiveOrgOfAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var issued = new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("accessToken").GetString());
        var fromToken = Guid.Parse(issued.Claims.Single(c => c.Type == ActiveOrgClaim).Value);
        Assert.Equal(fromToken, Guid.Parse(body.RootElement.GetProperty("user").GetProperty("activeOrgId").GetString()!));
        return fromToken;
    }

    private static async Task<List<Guid>> MembershipIdsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("memberships").EnumerateArray()
            .Select(item => Guid.Parse(item.GetProperty("orgId").GetString()!))
            .Order()
            .ToList();
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using OrganizationEntity = Zentory.Domain.Entities.Organization;

namespace Zentory.Tests.Integration.OwnedOrganizations;

// PLAN-0018 T25b (RF-74 a; SR-0018.35; D-30, D-45): max_owned_orgs serializado por User.OwnershipStamp entre
// POST /organizations y POST /organizations/{id}/restore del mismo usuario. CA-T25B-2 (parte de CA-125): un owner free
// sin organizaciones propias vigentes y una eliminada restaurable; dos operaciones a la vez dan exactamente un éxito y un
// 422 QUOTA_EXCEEDED, y al final es owner de una sola organización vigente.
public sealed class OwnedOrganizationsConcurrencyTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string CreateEndpoint = "/api/v1/organizations";

    // Repeticiones de las carreras reales: cada una con su owner, así ningún límite por usuario interfiere.
    private const int Repetitions = 10;

    private readonly ZentoryApiFactory              _factory;
    private readonly WebApplicationFactory<Program> _host;

    // Se ejecuta una vez justo antes de la escritura del comando, después de leer el usuario y contar: la otra operación
    // confirma en su propio contexto y el comando escribe con el OwnershipStamp ya viejo.
    private Func<Task>? _beforeSave;

    public OwnedOrganizationsConcurrencyTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp =>
                new HookedUnitOfWork(new UnitOfWork(sp.GetRequiredService<ZentoryDbContext>()), TakeHook));
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── Carreras reales (sin gancho) ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA125_CrearYRestaurarALaVez_OwnerFreeSinVigentes_UnExitoYUn422YQuedaOwnerDeUna()
    {
        for (var repetition = 0; repetition < Repetitions; repetition++)
        {
            var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
            using var http = ClientWithoutOrganization(scenario.Owner);

            var responses = await Task.WhenAll(
                CreateAsync(http, scenario.LegalType),
                OrganizationRestoreApi.RestoreAsync(http, scenario.Deleted.OrganizationId));

            await AssertOneSuccessAndOneQuotaExceededAsync(responses, $"repetición {repetition}");
            Assert.Equal(1, await ActiveOwnedCountAsync(scenario.Owner.UserId));
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task PLAN0018_CA125_DosCreacionesALaVez_OwnerFreeSinVigentes_UnExitoYUn422YQuedaOwnerDeUna()
    {
        for (var repetition = 0; repetition < Repetitions; repetition++)
        {
            var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
            using var http = ClientWithoutOrganization(scenario.Owner);

            var responses = await Task.WhenAll(
                CreateAsync(http, scenario.LegalType),
                CreateAsync(http, scenario.LegalType));

            await AssertOneSuccessAndOneQuotaExceededAsync(responses, $"repetición {repetition}");
            Assert.Equal(1, await ActiveOwnedCountAsync(scenario.Owner.UserId));
            foreach (var response in responses) response.Dispose();
        }
    }

    // ── Carreras deterministas (la otra operación confirma entre la lectura y la escritura) ─────────────────────

    [Fact]
    public async Task PLAN0018_CA125_CreacionPierdeAnteUnaRestauracionDelMismoOwner_VuelveAContarYResponde422SinCrear()
    {
        var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
        _beforeSave = () => OrganizationRestoreApi.RestoreDirectlyAsync(
            _host.Services, scenario.Deleted.OrganizationId, scenario.Owner.UserId);
        using var http = ClientWithoutOrganization(scenario.Owner);

        using var response = await CreateAsync(http, scenario.LegalType);

        await AssertQuotaExceededAsync(response);
        Assert.Equal([scenario.Deleted.OrganizationId], await ActiveOwnedIdsAsync(scenario.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA125_RestauracionPierdeAnteUnaCreacionDelMismoOwner_VuelveAContarYResponde422SinRestaurar()
    {
        var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
        var created = Guid.Empty;
        _beforeSave = async () => created = await CreateDirectlyAsync(scenario.Owner.UserId, scenario.LegalType);
        using var http = ClientWithoutOrganization(scenario.Owner);

        using var response = await OrganizationRestoreApi.RestoreAsync(http, scenario.Deleted.OrganizationId);

        await AssertQuotaExceededAsync(response);
        Assert.Equal([created], await ActiveOwnedIdsAsync(scenario.Owner.UserId));
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(_host.Services, scenario.Deleted.OrganizationId)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA125_CreacionPierdeAnteOtraCreacionDelMismoOwner_VuelveAContarYResponde422SinCrear()
    {
        var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
        var created = Guid.Empty;
        _beforeSave = async () => created = await CreateDirectlyAsync(scenario.Owner.UserId, scenario.LegalType);
        using var http = ClientWithoutOrganization(scenario.Owner);

        using var response = await CreateAsync(http, scenario.LegalType);

        await AssertQuotaExceededAsync(response);
        Assert.Equal([created], await ActiveOwnedIdsAsync(scenario.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA125_CreacionPierdeElSelloPeroAunCabe_ReintentaUnaVezYCrea()
    {
        // D-45: si al volver a contar todavía cabe, se reintenta (una sola vez). Aquí la otra escritura solo regenera el
        // sello, sin agregar organizaciones propias.
        var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
        _beforeSave = () => TouchOwnershipAsync(scenario.Owner.UserId);
        using var http = ClientWithoutOrganization(scenario.Owner);

        using var response = await CreateAsync(http, scenario.LegalType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await ActiveOwnedCountAsync(scenario.Owner.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA125_CrearOrganizacion_RegeneraElOwnershipStampEnLaMismaEscritura()
    {
        var scenario = await SeedOwnerWithOnlyDeletedOrganizationAsync();
        var before = await OwnershipStampAsync(scenario.Owner.UserId);
        using var http = ClientWithoutOrganization(scenario.Owner);

        using var response = await CreateAsync(http, scenario.LegalType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(before, await OwnershipStampAsync(scenario.Owner.UserId));
        Assert.Equal(1, await ActiveOwnedCountAsync(scenario.Owner.UserId));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record OwnerScenario(User Owner, OrganizationEntity Deleted, string LegalType);

    // Owner free cuya única organización propia (A) está eliminada y dentro del plazo: 0 vigentes, 1 restaurable.
    private async Task<OwnerScenario> SeedOwnerWithOnlyDeletedOrganizationAsync()
    {
        var tenants = await new TenantBuilder(_factory).ForA(a => a.WithPlan(Plan.Free)).BuildAsync();
        var a = tenants.A;
        await OrganizationRestoreApi.EnsureOwnedOrganizationLimitAsync(_host.Services, Plan.Free, a.LegalType, 1);
        await OrganizationRestoreApi.MarkDeletedAsync(_host.Services, a.OrganizationId, a.Owner.UserId, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(0, await ActiveOwnedCountAsync(a.Owner.UserId));
        return new OwnerScenario(a.Owner, a.Organization, a.LegalType);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient http, string legalType)
        => http.PostAsJsonAsync(CreateEndpoint, new { legalType, name = $"Nueva {Guid.NewGuid():N}", country = "CO" });

    // Creación hecha por otra operación del mismo owner, en su propio contexto: organización, membresía y sello nuevo.
    private async Task<Guid> CreateDirectlyAsync(Guid ownerId, string legalType)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var owner = await db.Users.SingleAsync(u => u.UserId == ownerId);
        var organization = OrganizationEntity.Create($"Concurrente {Guid.NewGuid():N}", legalType);
        organization.SetOwner(ownerId);
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(OrganizationMember.Create(organization.OrganizationId, ownerId, OrgRole.Owner));
        owner.TouchOwnership();
        await db.SaveChangesAsync();
        return organization.OrganizationId;
    }

    private async Task TouchOwnershipAsync(Guid ownerId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        (await db.Users.SingleAsync(u => u.UserId == ownerId)).TouchOwnership();
        await db.SaveChangesAsync();
    }

    private async Task<Guid> OwnershipStampAsync(Guid ownerId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.UserId == ownerId).Select(u => u.OwnershipStamp).SingleAsync();
    }

    // Organizaciones propias vigentes (el filtro global deja fuera las eliminadas), como las cuenta el comando.
    private async Task<List<Guid>> ActiveOwnedIdsAsync(Guid ownerId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Organizations.AsNoTracking()
            .Where(o => o.OwnerId == ownerId)
            .Select(o => o.OrganizationId)
            .ToListAsync();
    }

    private async Task<int> ActiveOwnedCountAsync(Guid ownerId) => (await ActiveOwnedIdsAsync(ownerId)).Count;

    private HttpClient ClientWithoutOrganization(User user)
    {
        var http = _host.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", OrganizationRestoreApi.TokenWithoutOrganization(_host.Services, user));
        return http;
    }

    private static async Task AssertOneSuccessAndOneQuotaExceededAsync(HttpResponseMessage[] responses, string label)
    {
        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.True(
            statuses.Count(s => s is HttpStatusCode.OK or HttpStatusCode.NoContent) == 1,
            $"{label}: se esperaba exactamente un éxito y hubo [{string.Join(", ", statuses.Select(s => (int)s))}].");
        await AssertQuotaExceededAsync(Assert.Single(responses, r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
    }

    private static async Task AssertQuotaExceededAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("QUOTA_EXCEEDED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(PlanLimits.FeatureKeys.MaxOwnedOrgs, body.RootElement.GetProperty("featureKey").GetString());
    }

    private Func<Task>? TakeHook() => Interlocked.Exchange(ref _beforeSave, null);
}

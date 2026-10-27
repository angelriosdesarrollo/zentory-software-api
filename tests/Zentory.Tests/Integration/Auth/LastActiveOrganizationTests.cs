using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Auth;

// PLAN-0018 RF-70 (CA-76; D-16, D-27, A-5): la última organización usada se persiste en POST /organization/switch y
// la usan POST /auth/login y POST /auth/google a través de IAuthSessionIssuer. Si la membresía ya no está vigente, la
// sesión entra a la primera organización propia y, si no hay, a la más antigua. También cubre R-1 de T5/T6: con un
// Bearer válido en esos endpoints anónimos, las membresías no se truncan a la organización del token.
//
// El host es la factory con IGoogleTokenValidator reemplazado por FakeGoogleTokenValidator; comparte la base InMemory.
public sealed class LastActiveOrganizationTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string LoginEndpoint   = "/api/v1/auth/login";
    private const string GoogleEndpoint  = "/api/v1/auth/google";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string SwitchEndpoint  = "/api/v1/organization/switch";
    private const string ActiveOrgClaim  = "active_org_id";

    private readonly FakeGoogleTokenValidator        _google = new();
    private readonly WebApplicationFactory<Program> _host;
    private readonly TestTokens                      _tokens;

    public LastActiveOrganizationTests(ZentoryApiFactory factory)
    {
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGoogleTokenValidator>();
            services.AddSingleton<IGoogleTokenValidator>(_google);
        }));
        _tokens = new TestTokens(_host.Services);
    }

    public void Dispose() => _host.Dispose();

    // ── Switch persiste la última organización ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA76_SwitchHaciaB_PersisteLastActiveOrganizationIdB()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();

        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);

        Assert.Equal(seed.OrgB.OrganizationId, await LastActiveAsync(seed.User.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA76_SwitchHaciaOrganizacionSinMembresia_Devuelve404SinTocarLastActiveOrganizationId()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();
        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);

        using var response = await SendAsync(HttpMethod.Post, SwitchEndpoint, TokenFor(seed.User, seed.OrgA),
            new { orgId = seed.ForeignOrg.OrganizationId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(seed.OrgB.OrganizationId, await LastActiveAsync(seed.User.UserId));
    }

    // ── Login con contraseña ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA76_LoginTrasSwitchHaciaB_EmiteTokenConActiveOrgIdB()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();

        // Control: antes del switch, el login entra a la organización propia.
        Assert.Equal(seed.OrgA.OrganizationId, await ActiveOrgOfAsync(await LoginAsync(seed.User)));

        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);

        Assert.Equal(seed.OrgB.OrganizationId, await ActiveOrgOfAsync(await LoginAsync(seed.User)));
    }

    [Fact]
    public async Task PLAN0018_CA76_LoginConMembresiaEnBBorrada_EntraALaOrganizacionPropia()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();
        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);
        await RemoveMembershipAsync(seed.OrgB.OrganizationId, seed.User.UserId);

        Assert.Equal(seed.OrgA.OrganizationId, await ActiveOrgOfAsync(await LoginAsync(seed.User)));
    }

    [Fact]
    public async Task PLAN0018_CA76_LoginSinOrganizacionPropiaYMembresiaEnBBorrada_EntraALaMasAntigua()
    {
        var seed = await SeedMemberWithoutOwnOrganizationAsync();
        await SwitchAsync(seed.User, seed.Oldest, seed.Last);
        Assert.Equal(seed.Last.OrganizationId, await ActiveOrgOfAsync(await LoginAsync(seed.User)));

        await RemoveMembershipAsync(seed.Last.OrganizationId, seed.User.UserId);

        Assert.Equal(seed.Oldest.OrganizationId, await ActiveOrgOfAsync(await LoginAsync(seed.User)));
    }

    // ── Login con Google ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA76_GoogleTrasSwitchHaciaB_EmiteTokenConActiveOrgIdB()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();
        Assert.Equal(seed.OrgA.OrganizationId, await ActiveOrgOfAsync(await GoogleLoginAsync(seed.User)));

        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);

        Assert.Equal(seed.OrgB.OrganizationId, await ActiveOrgOfAsync(await GoogleLoginAsync(seed.User)));
    }

    [Fact]
    public async Task PLAN0018_CA76_GoogleConMembresiaEnBBorrada_EntraALaOrganizacionPropia()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();
        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);
        await RemoveMembershipAsync(seed.OrgB.OrganizationId, seed.User.UserId);

        Assert.Equal(seed.OrgA.OrganizationId, await ActiveOrgOfAsync(await GoogleLoginAsync(seed.User)));
    }

    [Fact]
    public async Task PLAN0018_CA76_GoogleSinOrganizacionPropiaYMembresiaEnBBorrada_EntraALaMasAntigua()
    {
        var seed = await SeedMemberWithoutOwnOrganizationAsync();
        await SwitchAsync(seed.User, seed.Oldest, seed.Last);
        await RemoveMembershipAsync(seed.Last.OrganizationId, seed.User.UserId);

        Assert.Equal(seed.Oldest.OrganizationId, await ActiveOrgOfAsync(await GoogleLoginAsync(seed.User)));
    }

    // ── R-1: Bearer válido en los endpoints anónimos ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA76_LoginConBearerValidoDeA_EmiteTokenDeBYListaTodasLasMembresias()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();
        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);

        // Con el Bearer de A, el filtro de tenant de OrganizationMember solo dejaría ver A.
        using var response = await SendAsync(HttpMethod.Post, LoginEndpoint, TokenFor(seed.User, seed.OrgA),
            new { email = seed.User.Email, password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seed.OrgB.OrganizationId, await ActiveOrgOfAsync(response));
        Assert.Equal(new[] { seed.OrgA.OrganizationId, seed.OrgB.OrganizationId }.Order(), await MembershipIdsAsync(response));
    }

    [Fact]
    public async Task PLAN0018_CA76_RefreshConBearerValidoDeA_RespetaActiveOrgIdBYListaTodasLasMembresias()
    {
        var seed = await SeedOwnerOfAMemberOfBAsync();
        var refreshToken = await SeedRefreshTokenAsync(seed.User);

        using var response = await SendAsync(HttpMethod.Post, RefreshEndpoint, TokenFor(seed.User, seed.OrgA),
            new { refreshToken, activeOrgId = seed.OrgB.OrganizationId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seed.OrgB.OrganizationId, await ActiveOrgOfAsync(response));
        Assert.Equal(new[] { seed.OrgA.OrganizationId, seed.OrgB.OrganizationId }.Order(), await MembershipIdsAsync(response));
    }

    [Fact]
    public async Task PLAN0018_CA76_RefreshSinActiveOrgId_NoUsaLastActiveYEntraALaOrganizacionPropia()
    {
        // D-27: refresh respeta la organización pedida (RF-16); LastActiveOrganizationId es solo de login y Google.
        var seed = await SeedOwnerOfAMemberOfBAsync();
        await SwitchAsync(seed.User, seed.OrgA, seed.OrgB);
        var refreshToken = await SeedRefreshTokenAsync(seed.User);

        using var response = await SendAsync(HttpMethod.Post, RefreshEndpoint, token: null, new { refreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(seed.OrgA.OrganizationId, await ActiveOrgOfAsync(response));
    }

    // ── Migración M0b ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA76_MigracionM0b_AgregaLastActiveOrganizationIdNullableSinFk()
    {
        using var db = new ZentoryDbContext(new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql("Host=unused").Options);

        var migrations = db.GetService<IMigrationsAssembly>().Migrations;
        var (id, type) = Assert.Single(migrations, m => m.Key.EndsWith("_AddUserLastActiveOrganization", StringComparison.Ordinal));
        Assert.True(string.CompareOrdinal(id, migrations.Keys.Single(k => k.EndsWith("_InitialCreate", StringComparison.Ordinal))) > 0);

        var migration = (Migration)Activator.CreateInstance(type.AsType())!;
        var addColumn = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal("users", addColumn.Table);
        Assert.Equal("last_active_organization_id", addColumn.Name);
        Assert.True(addColumn.IsNullable);
        Assert.Equal(typeof(Guid), addColumn.ClrType);

        var users = db.Model.FindEntityType(typeof(User))!;
        var property = users.FindProperty(nameof(User.LastActiveOrganizationId))!;
        Assert.True(property.IsNullable);
        Assert.Empty(property.GetContainingForeignKeys());
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private const string Password = "Clave-De-Prueba-2026!";

    private sealed record OwnerSeed(User User, Organization OrgA, Organization OrgB, Organization ForeignOrg);

    private sealed record MemberSeed(User User, Organization Oldest, Organization Last);

    // Usuario owner de A y member de B (de otro owner), con contraseña y Google. ForeignOrg no es suya.
    private async Task<OwnerSeed> SeedOwnerOfAMemberOfBAsync()
    {
        var other = NewUser("otro");
        var user  = NewUser("persona");
        var orgA = NewOrganization("A", user);
        var orgB = NewOrganization("B", other);
        var foreign = NewOrganization("D", other);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Users.AddRange(user, other);
        db.Organizations.AddRange(orgA, orgB, foreign);
        AddMember(db, orgA, user, OrgRole.Owner, DateTime.UtcNow.AddDays(-10));
        AddMember(db, orgB, other, OrgRole.Owner, DateTime.UtcNow.AddDays(-30));
        AddMember(db, orgB, user, OrgRole.Member, DateTime.UtcNow.AddDays(-20));
        AddMember(db, foreign, other, OrgRole.Owner, DateTime.UtcNow.AddDays(-30));
        await db.SaveChangesAsync();

        return new OwnerSeed(user, orgA, orgB, foreign);
    }

    // Usuario sin organización propia: member de Oldest (la más antigua), de Middle y de Last (la que elige con switch).
    private async Task<MemberSeed> SeedMemberWithoutOwnOrganizationAsync()
    {
        var other = NewUser("otro");
        var user  = NewUser("miembro");
        var oldest = NewOrganization("C", other);
        var middle = NewOrganization("E", other);
        var last   = NewOrganization("B", other);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Users.AddRange(user, other);
        db.Organizations.AddRange(oldest, middle, last);
        foreach (var org in new[] { oldest, middle, last })
            AddMember(db, org, other, OrgRole.Owner, DateTime.UtcNow.AddDays(-60));
        // La más reciente se agrega primero para que el orden de inserción no coincida con el de JoinedAt.
        AddMember(db, last, user, OrgRole.Member, DateTime.UtcNow.AddDays(-1));
        AddMember(db, middle, user, OrgRole.Admin, DateTime.UtcNow.AddDays(-5));
        AddMember(db, oldest, user, OrgRole.Member, DateTime.UtcNow.AddDays(-40));
        await db.SaveChangesAsync();

        return new MemberSeed(user, oldest, last);
    }

    private static User NewUser(string label)
    {
        var localPart = $"{label}-{Guid.NewGuid():N}";
        return User.Create($"{localPart}@zentory.test", "Prueba", label, OrgRole.Member,
            passwordHash: BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4),
            googleId: $"google-{Guid.NewGuid():N}");
    }

    private static Organization NewOrganization(string label, User owner)
    {
        var org = Organization.Create($"Org {label} {Guid.NewGuid():N}", LegalType.Empresa);
        org.SetOwner(owner.UserId);
        return org;
    }

    private static void AddMember(ZentoryDbContext db, Organization org, User user, string role, DateTime joinedAt)
    {
        var member = OrganizationMember.Create(org.OrganizationId, user.UserId, role);
        db.OrganizationMembers.Add(member);
        db.Entry(member).Property(m => m.JoinedAt).CurrentValue = joinedAt;
    }

    private async Task RemoveMembershipAsync(Guid organizationId, Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var membership = await db.OrganizationMembers
            .SingleAsync(m => m.OrganizationId == organizationId && m.UserId == userId && m.DeletedAt == null);
        membership.SoftDelete();
        await db.SaveChangesAsync();
    }

    private async Task<Guid?> LastActiveAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => u.LastActiveOrganizationId)
            .SingleAsync();
    }

    private Task<string> SeedRefreshTokenAsync(User user) => AuthSessionApi.SeedRefreshTokenAsync(_host.Services, user);

    // El rol del token no importa: el middleware lee el de la base (SR-0018.7).
    private string TokenFor(User user, Organization org) => _tokens.ForUser(user, org, OrgRole.Owner, Plan.Free);

    private async Task SwitchAsync(User user, Organization from, Organization to)
    {
        using var response = await SendAsync(HttpMethod.Post, SwitchEndpoint, TokenFor(user, from),
            new { orgId = to.OrganizationId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> LoginAsync(User user)
    {
        var response = await SendAsync(HttpMethod.Post, LoginEndpoint, token: null,
            new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private async Task<HttpResponseMessage> GoogleLoginAsync(User user)
    {
        var idToken = _google.Register(new GoogleUserInfo(user.GoogleId!, user.Email, user.FirstName, user.LastName, EmailVerified: true));
        var response = await SendAsync(HttpMethod.Post, GoogleEndpoint, token: null, new { idToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }

    private static async Task<Guid> ActiveOrgOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var issued = new JwtSecurityTokenHandler().ReadJwtToken(body.RootElement.GetProperty("accessToken").GetString());
        var fromToken = Guid.Parse(issued.Claims.Single(c => c.Type == ActiveOrgClaim).Value);
        Assert.Equal(fromToken, Guid.Parse(body.RootElement.GetProperty("user").GetProperty("activeOrgId").GetString()!));
        return fromToken;
    }

    private static async Task<IEnumerable<Guid>> MembershipIdsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("memberships").EnumerateArray()
            .Select(item => Guid.Parse(item.GetProperty("orgId").GetString()!))
            .Order()
            .ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object body)
    {
        using var client = _host.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}

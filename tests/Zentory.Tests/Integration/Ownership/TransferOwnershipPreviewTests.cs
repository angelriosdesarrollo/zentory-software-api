using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-100 (RF-77; SR-0018.46; D-33) sobre el host real con el catálogo de planes sembrado:
// GET /organization/transfer-ownership/preview. El owner O es studio con una organización empresa de 7 miembros
// vigentes; el admin A es pro y no es owner de nada. El aviso no cambia nada y no expone conteos del destino.
public sealed class TransferOwnershipPreviewTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword  = "Owner-Aviso-2026!";
    private const string MemberPassword = "Miembro-Aviso-2026!";

    // Propiedades del contrato de RF-77: ninguna con conteos de organizaciones del destino (SR-0018.46).
    private static readonly string[] ContractProperties =
        ["currentPlan", "newPlan", "lostFeatures", "exceededLimits", "blocked", "blockReason", "previewHash"];

    [Fact]
    public async Task PLAN0018_CA100_Preview_OwnerStudioHaciaAdminPro_DevuelvePlanesFuncionesPerdidasYLimiteDeMiembrosSinCambiarNada()
    {
        var tenants = await SevenMemberOrganizationAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        await OwnershipTransferApi.SeedOwnSubscriptionAsync(factory.Services, admin, tenants.A.Organization, Plan.Pro);
        var rowsBefore     = await OrganizationDeletionApi.RowCountsAsync(factory.Services);
        var activityBefore = await OwnershipTransferApi.ActivityCountAsync(factory.Services, tenants.A.OrganizationId);
        using var http = tenants.ClientFor(tenants.A);

        using var response = await OwnershipTransferApi.PreviewAsync(http, admin.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(Plan.Studio, root.GetProperty("currentPlan").GetString());
        Assert.Equal(Plan.Pro, root.GetProperty("newPlan").GetString());

        // Las funciones studio de empresa salen del catálogo (PlanFeature), no del frontend.
        var lost = root.GetProperty("lostFeatures").EnumerateArray().Select(f => f.GetString()).ToList();
        var studioFeatures = await FeaturesOfAsync(Plan.Studio, LegalType.Empresa);
        Assert.NotEmpty(studioFeatures);
        Assert.All(studioFeatures, feature => Assert.Contains(feature, lost));
        Assert.DoesNotContain(await FeaturesOfAsync(Plan.Pro, LegalType.Empresa), feature => lost.Contains(feature));

        var exceeded = root.GetProperty("exceededLimits").EnumerateArray().ToList();
        var members = Assert.Single(exceeded,
            l => l.GetProperty("featureKey").GetString() == PlanLimits.FeatureKeys.MaxOrgMembers);
        Assert.Equal(7, members.GetProperty("usage").GetInt32());
        Assert.Equal(5, members.GetProperty("limit").GetInt32());

        Assert.False(root.GetProperty("blocked").GetBoolean());
        var hash = root.GetProperty("previewHash").GetString();
        Assert.Matches("^[0-9a-f]{64}$", hash);

        // No cambia nada.
        Assert.Equal(rowsBefore, await OrganizationDeletionApi.RowCountsAsync(factory.Services));
        Assert.Equal(activityBefore, await OwnershipTransferApi.ActivityCountAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));

        // Mismo estado, mismo hash: es el acuse que exige la propuesta (D-33).
        Assert.Equal(hash, await OwnershipTransferApi.PreviewHashAsync(http, admin.UserId));
    }

    [Fact]
    public async Task PLAN0018_CA100_Preview_DestinoFreeQueYaEsOwnerDeUnaOrganizacion_DevuelveBlockedQuotaExceeded()
    {
        var tenants = await SevenMemberOrganizationAsync(configureB: b => b.WithPlan(Plan.Free));
        var freeOwner = tenants.B.Owner;
        await OwnershipTransferApi.AddMemberAsync(
            factory.Services, tenants.A.OrganizationId, freeOwner, OrgRole.Member, tenants.A.Owner.UserId);
        await OwnershipTransferApi.MarkEmailVerifiedAsync(factory.Services, freeOwner);
        using var http = tenants.ClientFor(tenants.A);

        using var response = await OwnershipTransferApi.PreviewAsync(http, freeOwner.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Plan.Free, body.RootElement.GetProperty("newPlan").GetString());
        Assert.True(body.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal(OwnershipTransferPreview.QuotaExceededReason, body.RootElement.GetProperty("blockReason").GetString());
    }

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA100_Preview_LlamadoPorAdminOMember_Responde403AccessDenied(string role)
    {
        var tenants = await SevenMemberOrganizationAsync();
        var target = tenants.A.Members.First(m => m.Role != role).User;
        using var http = tenants.ClientFor(tenants.A, tenants.A.Member(role));

        using var response = await OwnershipTransferApi.PreviewAsync(http, target.UserId);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    [Fact]
    public async Task PLAN0018_CA100_Preview_RespuestaSinConteosDeOrganizacionesDelDestino_SoloLasPropiedadesDelContrato()
    {
        // SR-0018.46: test de contrato sobre el DTO y sobre el JSON, con un destino bloqueado y otro libre.
        var dtoProperties = typeof(OwnershipTransferPreviewDto).GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .Order(StringComparer.Ordinal);
        Assert.Equal(ContractProperties.Order(StringComparer.Ordinal), dtoProperties);
        Assert.Equal(["featureKey", "limit", "usage"],
            typeof(ExceededLimitDto).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .Order(StringComparer.Ordinal));

        var tenants = await SevenMemberOrganizationAsync(configureB: b => b.WithPlan(Plan.Free));
        await OwnershipTransferApi.AddMemberAsync(
            factory.Services, tenants.A.OrganizationId, tenants.B.Owner, OrgRole.Member, tenants.A.Owner.UserId);
        await OwnershipTransferApi.MarkEmailVerifiedAsync(factory.Services, tenants.B.Owner);
        using var http = tenants.ClientFor(tenants.A);

        foreach (var target in new[] { tenants.B.Owner.UserId, tenants.A.Member(OrgRole.Admin).UserId })
        {
            using var response = await OwnershipTransferApi.PreviewAsync(http, target);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var names = body.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            Assert.All(names, name => Assert.Contains(name, ContractProperties));
            Assert.DoesNotContain(names, name => name.Contains("owned", StringComparison.OrdinalIgnoreCase)
                                              || name.Contains("count", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task PLAN0018_CA100_Preview_DestinoSinMembresiaDeA_Responde404_HaciaUnoMismo422_SinVerificar403()
    {
        var tenants = await SevenMemberOrganizationAsync();
        using var http = tenants.ClientFor(tenants.A);

        // Un miembro de B (su owner) y un id inexistente: 404 con el mismo cuerpo.
        using (var ofB = await OwnershipTransferApi.PreviewAsync(http, tenants.B.Owner.UserId))
        using (var missing = await OwnershipTransferApi.PreviewAsync(http, Guid.NewGuid()))
        {
            Assert.Equal(HttpStatusCode.NotFound, ofB.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal(await missing.Content.ReadAsStringAsync(), await ofB.Content.ReadAsStringAsync());
        }

        using (var self = await OwnershipTransferApi.PreviewAsync(http, tenants.A.Owner.UserId))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, self.StatusCode);
            Assert.Contains(OwnershipTransferPreview.UserIdField, await self.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var unverified = await UnverifiedMemberAsync(tenants.A);
        using var notVerified = await OwnershipTransferApi.PreviewAsync(http, unverified.UserId);
        await ForbiddenAssert.HasCodeAsync(notVerified, "EMAIL_NOT_VERIFIED");
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Owner studio (por defecto) con 6 miembros más: 7 vigentes, verificados.
    private async Task<TenantPair> SevenMemberOrganizationAsync(Action<TenantOptions>? configureB = null)
        => await OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword,
            configureA: a => a.WithMember(OrgRole.Member).WithMember(OrgRole.Member).WithMember(OrgRole.Admin)
                .WithMember(OrgRole.Member),
            configureB: configureB);

    private async Task<User> UnverifiedMemberAsync(SeededTenant tenant)
    {
        var user = User.Create($"sin-verificar-{Guid.NewGuid():N}@zentory.test", "Sin", "Verificar", OrgRole.Member);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.Users.Add(user);
        db.OrganizationMembers.Add(OrganizationMember.Create(tenant.OrganizationId, user.UserId, OrgRole.Member,
            invitedBy: tenant.Owner.UserId));
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<List<string>> FeaturesOfAsync(string plan, string legalType)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.PlanFeatures
            .Where(f => f.LegalType == legalType)
            .Join(db.BillingPlans, f => f.PlanId, p => p.Id, (f, p) => new { p.Name, f.Text })
            .Where(x => x.Name == plan)
            .Select(x => x.Text)
            .ToListAsync();
    }
}

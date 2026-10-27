using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Entities.Billing;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Security.Idor;

// PLAN-0018 CA-2 (cierre en T31) sobre el host real: los endpoints de los cuatro módulos (MOD-0001, MOD-0002, MOD-0003,
// MOD-0019) que reciben o resuelven un recurso y que aún no tenían caso IDOR. Los de miembros, roles, restore, listado de
// eliminadas y aceptar/rechazar la transferencia ya lo tienen en sus clases (ChangeRoleTests, RemoveMemberTests,
// RolesCatalogTests, RestoreTests, AcceptOwnershipTransferTests).
//
// Siempre actúa A con su token; B tiene datos con marcas propias. Formas:
//   - lecturas de la organización activa (profile, settings, plan, billing-history, plan-limits) y listados del usuario
//     (/me/organizations, /me/ownership-transfers): IdorAssert.ListExcludesAsync, con control positivo de A;
//   - escrituras que resuelven el recurso desde la organización activa (PATCH profile/settings, DELETE /organization,
//     DELETE transfer-ownership, invite): IdorAssert.LeavesBUnchangedAsync sobre las filas de B;
//   - un id de B en el cuerpo o la query (switch, preview, proponer): IdorAssert.CannotAccessAsync, 404 sin efecto.
public sealed class IdentityAccessIdorTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword  = "Owner-Idor-Cierre-2026!";
    private const string MemberPassword = "Miembro-Idor-Cierre-2026!";

    private const string ProfileEndpoint = "/api/v1/organization/profile";

    // ── Lecturas de la organización activa ──────────────────────────────────────────────────────────────────────

    [Theory]
    [IdorCase("GET", "api/v1/organization/profile")]
    [IdorCase("GET", "api/v1/organization/settings")]
    [IdorCase("GET", "api/v1/organization/plan-limits")]
    [IdorCase("GET", "api/v1/organization/plan")]
    [InlineData("/api/v1/organization/profile")]
    [InlineData("/api/v1/organization/settings")]
    [InlineData("/api/v1/organization/plan-limits")]
    [InlineData("/api/v1/organization/plan")]
    public async Task PLAN0018_CA2_IdorAssert_LecturaDeLaOrganizacionActivaConTokenDeA_NoIncluyeDatosDeB(string path)
    {
        var tenants = await OrganizationsAsync();
        var markerOfA = await SeedLegalNameAsync(tenants.A);
        var markerOfB = await SeedLegalNameAsync(tenants.B);
        using var http = tenants.ClientFor(tenants.A);

        var body = await IdorAssert.ListExcludesAsync(http, path, [tenants.B.OrganizationId, tenants.B.Owner.UserId],
            tenants.B.Organization.Name, markerOfB, tenants.B.Owner.Email);

        // Control positivo donde el endpoint expone algo propio de la organización.
        if (path == ProfileEndpoint)
        {
            Assert.Contains(tenants.A.OrganizationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(markerOfA, body, StringComparison.Ordinal);
        }
        else if (path.EndsWith("/settings", StringComparison.Ordinal))
        {
            Assert.Contains(markerOfA, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    [IdorCase("GET", "api/v1/organization/billing-history")]
    public async Task PLAN0018_CA2_IdorAssert_BillingHistoryConTokenDeA_NoIncluyeLasFacturasPagadasDeB()
    {
        var tenants = await OrganizationsAsync();
        await SeedPaidBillingInvoiceAsync(tenants.A, 1234.56m);
        var invoiceOfB = await SeedPaidBillingInvoiceAsync(tenants.B, 9876.54m);
        using var http = tenants.ClientFor(tenants.A);

        var body = await IdorAssert.ListExcludesAsync(http, "/api/v1/organization/billing-history",
            [invoiceOfB.Id, tenants.B.OrganizationId], "9876.54");

        Assert.Contains("1234.56", body, StringComparison.Ordinal);
    }

    // ── Listados del usuario ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    [IdorCase("GET", "api/v1/me/organizations")]
    public async Task PLAN0018_CA2_IdorAssert_MisOrganizacionesConTokenDeA_NoIncluyeLaOrganizacionDeB()
    {
        var tenants = await OrganizationsAsync();
        using var http = tenants.ClientFor(tenants.A);

        var body = await IdorAssert.ListExcludesAsync(http, "/api/v1/me/organizations", [tenants.B.OrganizationId],
            tenants.B.Organization.Name);

        Assert.Contains(tenants.A.OrganizationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [IdorCase("GET", "api/v1/me/ownership-transfers")]
    public async Task PLAN0018_CA2_IdorAssert_MisTransferenciasConTokenDeA_NoIncluyeLaPropuestaPendienteDeB()
    {
        var tenants = await OrganizationsAsync();
        var adminOfA = tenants.A.Member(OrgRole.Admin);
        var transferToA = await PendingTransferSeed.SeedAsync(factory.Services, tenants.A, adminOfA);
        var transferOfB = await PendingTransferSeed.SeedAsync(factory.Services, tenants.B, tenants.B.Member(OrgRole.Admin));
        using var http = tenants.ClientFor(tenants.A, adminOfA);

        var body = await IdorAssert.ListExcludesAsync(http, OwnershipTransferApi.MyTransfersEndpoint, [transferOfB.Id],
            tenants.B.Organization.Name);

        Assert.Contains(transferToA.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // ── Escrituras que resuelven el recurso desde la organización activa ────────────────────────────────────────

    [Theory]
    [IdorCase("PATCH", "api/v1/organization/profile")]
    [IdorCase("PATCH", "api/v1/organization/settings")]
    [InlineData("/api/v1/organization/profile", """{"legalName":"Razón social escrita por A"}""")]
    [InlineData("/api/v1/organization/settings", "{\"" + OrgSettingKey.ProposalValidity + "\":\"21\"}")]
    public async Task PLAN0018_CA2_IdorAssert_PatchDeLaOrganizacionConTokenDeA_NoCambiaLosSettingsDeB(string path, string body)
    {
        var tenants = await OrganizationsAsync();
        await SeedLegalNameAsync(tenants.B);
        var organizationOfB = tenants.B.OrganizationId;
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.LeavesBUnchangedAsync<OrganizationSettings>(factory.Services, http,
            Json(HttpMethod.Patch, path, body), s => s.OrganizationId == organizationOfB);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    [IdorCase("DELETE", "api/v1/organization")]
    public async Task PLAN0018_CA2_IdorAssert_DeleteOrganizationConTokenDeA_NombreDeB422_ElDeA204_SinTocarB()
    {
        var tenants = await OrganizationsAsync();
        var organizationOfB = tenants.B.OrganizationId;
        using var http = tenants.ClientFor(tenants.A);

        // Con el nombre de B: no coincide con la organización activa (422), y B queda igual.
        var withNameOfB = await IdorAssert.LeavesBUnchangedAsync<Organization>(factory.Services, http,
            DeleteOrganization(tenants.B.Organization.Name), o => o.OrganizationId == organizationOfB);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, withNameOfB);
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).DeletedAt);

        // Con el nombre de A: se elimina solo A.
        var withNameOfA = await IdorAssert.LeavesBUnchangedAsync<Organization>(factory.Services, http,
            DeleteOrganization(tenants.A.Organization.Name), o => o.OrganizationId == organizationOfB);
        Assert.Equal(HttpStatusCode.NoContent, withNameOfA);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).DeletedAt);
    }

    [Fact]
    [IdorCase("DELETE", "api/v1/organization/transfer-ownership")]
    public async Task PLAN0018_CA2_IdorAssert_CancelarTransferenciaConTokenDeA_NoCancelaLaPendienteDeB()
    {
        var tenants = await OrganizationsAsync();
        var transferOfB = await PendingTransferSeed.SeedAsync(factory.Services, tenants.B, tenants.B.Member(OrgRole.Admin));
        var organizationOfB = tenants.B.OrganizationId;
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.LeavesBUnchangedAsync<OrganizationOwnershipTransfer>(factory.Services, http,
            new HttpRequestMessage(HttpMethod.Delete, OwnershipTransferApi.TransferEndpoint),
            t => t.OrganizationId == organizationOfB);

        // Idempotente (RF-64 c3): 204 aunque A no tenga propuesta; la de B sigue pendiente.
        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(OwnershipTransferStatus.Pending,
            (await OwnershipTransferApi.TransferAsync(factory.Services, transferOfB.Id)).Status);
    }

    [Fact]
    [IdorCase("POST", "api/v1/organization/members/invite")]
    public async Task PLAN0018_CA2_IdorAssert_InvitarConTokenDeA_AlOwnerDeB_NoTocaMembresiasNiInvitacionesDeB()
    {
        var tenants = await OrganizationsAsync();
        var organizationOfB = tenants.B.OrganizationId;
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.LeavesBUnchangedAsync<OrganizationMember>(factory.Services, http,
            Json(HttpMethod.Post, MemberManagementApi.MembersEndpoint + "/invite",
                $$"""{"email":"{{tenants.B.Owner.Email}}","roleId":"{{OrgRole.Member}}"}"""),
            m => m.OrganizationId == organizationOfB);

        // La invitación es a A (202) y no crea nada en B.
        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal("[]", await IdorAssert.SnapshotWhereAsync<OrganizationInvitation>(factory.Services,
            i => i.OrganizationId == organizationOfB));
    }

    // ── Un id de B en el cuerpo o la query ──────────────────────────────────────────────────────────────────────

    [Fact]
    [IdorCase("POST", "api/v1/organization/switch")]
    public async Task PLAN0018_CA2_IdorAssert_SwitchALaOrganizacionDeBConTokenDeA_Responde404SinEfecto()
    {
        var tenants = await OrganizationsAsync();
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.CannotAccessAsync<Organization>(factory, http,
            Json(HttpMethod.Post, AuthSessionApi.SwitchEndpoint, $$"""{"orgId":"{{tenants.B.OrganizationId}}"}"""),
            tenants.B.OrganizationId);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    [IdorCase("GET", "api/v1/organization/transfer-ownership/preview")]
    public async Task PLAN0018_CA2_IdorAssert_AvisoDeTransferenciaHaciaUnMiembroDeB_Responde404SinEfecto()
    {
        var tenants = await OrganizationsAsync();
        var adminOfB = tenants.B.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A);

        var status = await IdorAssert.CannotAccessAsync<User>(factory, http,
            new HttpRequestMessage(HttpMethod.Get, $"{OwnershipTransferApi.PreviewEndpoint}?userId={adminOfB.UserId}"),
            adminOfB.UserId);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    [IdorCase("POST", "api/v1/organization/transfer-ownership")]
    public async Task PLAN0018_CA2_IdorAssert_ProponerTransferenciaAUnMiembroDeB_Responde404SinPropuesta()
    {
        var tenants = await OrganizationsAsync();
        var adminOfB = tenants.B.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A);

        // Con la contraseña correcta del owner de A: la reautenticación pasa y el destino de B da 404 (RF-64 a).
        var status = await IdorAssert.CannotAccessAsync<User>(factory, http,
            Json(HttpMethod.Post, OwnershipTransferApi.TransferEndpoint,
                $$"""{"userId":"{{adminOfB.UserId}}","acknowledgedPreviewHash":"hash-cualquiera","currentPassword":"{{OwnerPassword}}"}"""),
            adminOfB.UserId);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.B.OrganizationId));
        Assert.Equal(OrgRole.Admin,
            (await MemberManagementApi.MembershipAsync(factory.Services, tenants.B.OrganizationId, adminOfB.UserId))!.Role);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // A y B empresa + studio, cada una con owner (contraseña), admin y member verificados.
    private Task<TenantPair> OrganizationsAsync()
        => OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword,
            configureB: b => b.WithLegalType(LegalType.Empresa).WithOwnerPassword(OwnerPassword)
                .WithMember(OrgRole.Admin).WithMember(OrgRole.Member).WithMemberPassword(MemberPassword));

    private async Task<string> SeedLegalNameAsync(SeededTenant tenant)
    {
        var marker = $"Razon social {tenant.Organization.Name} {Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        db.OrganizationSettings.Add(OrganizationSettings.Set(tenant.OrganizationId, OrgSettingKey.ProfileLegalName, marker));
        await db.SaveChangesAsync();
        return marker;
    }

    private async Task<BillingInvoice> SeedPaidBillingInvoiceAsync(SeededTenant tenant, decimal amount)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var customer = BillingCustomer.Create(tenant.OrganizationId, $"test_{Guid.NewGuid():N}", tenant.Owner.Email,
            tenant.Owner.FirstName);
        var invoice = BillingInvoice.Create(tenant.OrganizationId, customer.Id, amount);
        invoice.MarkPaid(DateTime.UtcNow.AddDays(-1));
        db.BillingCustomers.Add(customer);
        db.BillingInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }


    private static HttpRequestMessage DeleteOrganization(string confirmName)
        => new(HttpMethod.Delete, OrganizationDeletionApi.Endpoint)
        {
            Content = JsonContent.Create(new Dictionary<string, string>
            {
                ["confirmName"]     = confirmName,
                ["currentPassword"] = OwnerPassword,
            }),
        };

    private static HttpRequestMessage Json(HttpMethod method, string path, string body)
        => new(method, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

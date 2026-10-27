using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Security.Idor;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 T30c sobre el host real con el reloj simulado y el verificador espía: el destino acepta la transferencia
// (RF-83 b–e). Escenario de CA-89: organización empresa + studio con owner O y admin A verificado (plan propio pro, owner
// de nada). Cubre CA-89 (aviso del destino, aceptación, 404 uniforme, vencimiento, PREVIEW_STALE, TRANSFER_INVALID),
// CA-114 (credencial del destino), CA-125 (límite del destino al aceptar), CA-55 (roles tras la transferencia) y CA-20
// (las rutas del destino sirven desde otra organización activa).
public sealed class AcceptOwnershipTransferTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Aceptar-2026!";
    private const string MemberPassword = "Miembro-Aceptar-2026!";
    private const string WrongPassword  = "Incorrecta-Aceptar-Trampa-2026!";

    private const string StudioEndpoint = "/api/v1/collaborators";

    private readonly ZentoryApiFactory              _factory;
    private readonly FakeTimeProvider               _time;
    private readonly ReauthVerifierSpy              _spy = new();
    private readonly WebApplicationFactory<Program> _host;

    public AcceptOwnershipTransferTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_time);
            _spy.Install(services);
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── CA-89: aceptación ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA89_Aceptar_DestinoConSuContrasena_Responde204OwnerRolesYPlanCambianRefreshRevocadosYActivityLog()
    {
        var (tenants, admin) = await SeedAsync();
        var owner = tenants.A.Owner;
        using var ownerHttp = Client(tenants, owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var ownerRefresh = await AuthSessionApi.SeedRefreshTokenAsync(_host.Services, owner);
        var adminRefresh = await AuthSessionApi.SeedRefreshTokenAsync(_host.Services, admin);
        using (var studio = await ownerHttp.GetAsync(StudioEndpoint))
            Assert.Equal(HttpStatusCode.OK, studio.StatusCode);
        var rowsBefore = await OrganizationDeletionApi.RowCountsAsync(_host.Services);

        using var response = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(admin.UserId, (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal(OrgRole.Owner, roles[admin.UserId]);
        Assert.Equal(OrgRole.Admin, roles[owner.UserId]);
        var transfer = await OwnershipTransferApi.TransferAsync(_host.Services, transferId);
        Assert.Equal((OwnershipTransferStatus.Accepted, _time.GetUtcNow().UtcDateTime), (transfer.Status, transfer.ResolvedAt));
        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);

        // Los refresh tokens de los dos quedan revocados; sus access tokens toman los roles nuevos en la siguiente petición.
        Assert.All(await AuthSessionApi.RefreshTokensOfAsync(_host.Services, owner.UserId), rt => Assert.True(rt.IsRevoked));
        Assert.All(await AuthSessionApi.RefreshTokensOfAsync(_host.Services, admin.UserId), rt => Assert.True(rt.IsRevoked));
        using var anonymous = _host.CreateClient();
        foreach (var refresh in new[] { ownerRefresh, adminRefresh })
            using (var refreshed = await AuthSessionApi.RefreshAsync(anonymous, refresh))
                await AuthSessionApi.AssertRefreshTokenInvalidAsync(refreshed);

        using (var newOwner = await OwnershipTransferApi.CancelAsync(adminHttp))
            Assert.Equal(HttpStatusCode.NoContent, newOwner.StatusCode);
        using (var previousOwner = await OwnershipTransferApi.CancelAsync(ownerHttp))
            await ForbiddenAssert.IsAccessDeniedAsync(previousOwner);

        // El plan efectivo es el pro del nuevo owner: lo de studio responde PLAN_REQUIRED sin borrar ningún dato (RF-75).
        using (var studio = await adminHttp.GetAsync(StudioEndpoint))
            await ForbiddenAssert.IsPlanRequiredAsync(studio, Plan.Studio);
        Assert.Equal(rowsBefore, await OrganizationDeletionApi.RowCountsAsync(_host.Services));

        var log = Assert.Single(await OwnershipTransferApi.ActivityAsync(_host.Services, tenants.A.OrganizationId),
            a => a.Action == AcceptOwnershipTransferCommandHandler.LogAction);
        Assert.Equal((admin.UserId, transferId), (log.UserId, log.EntityId));

        using var again = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA89_AvisoDelDestino_ListaLaPropuestaConPlanesYOrganizacionesPropias_YNoSeLaMuestraAlOwnerNiAOtroMiembro()
    {
        var (tenants, admin) = await SeedAsync();
        var owner = tenants.A.Owner;
        using var ownerHttp = Client(tenants, owner);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var transfer = await OwnershipTransferApi.TransferAsync(_host.Services, transferId);
        using var adminHttp = Client(tenants, admin);

        using var response = await OwnershipTransferApi.MyTransfersAsync(adminHttp);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(["id", "organizationId", "organizationName", "fromUserName", "expiresAt", "preview"],
            item.EnumerateObject().Select(p => p.Name));
        Assert.Equal(transferId, item.GetProperty("id").GetGuid());
        Assert.Equal(tenants.A.OrganizationId, item.GetProperty("organizationId").GetGuid());
        Assert.Equal(tenants.A.Organization.Name, item.GetProperty("organizationName").GetString());
        Assert.Equal($"{owner.FirstName} {owner.LastName}", item.GetProperty("fromUserName").GetString());
        Assert.Equal(transfer.ExpiresAt, item.GetProperty("expiresAt").GetDateTime().ToUniversalTime());

        var preview = item.GetProperty("preview");
        Assert.Equal(["currentPlan", "newPlan", "lostFeatures", "exceededLimits", "ownedOrganizations"],
            preview.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Plan.Studio, preview.GetProperty("currentPlan").GetString());
        Assert.Equal(Plan.Pro, preview.GetProperty("newPlan").GetString());
        Assert.NotEmpty(preview.GetProperty("lostFeatures").EnumerateArray());
        Assert.Equal(JsonValueKind.Array, preview.GetProperty("exceededLimits").ValueKind);
        var owned = preview.GetProperty("ownedOrganizations");
        Assert.Equal(0, owned.GetProperty("usage").GetInt32());
        Assert.True(owned.GetProperty("limit").ValueKind is JsonValueKind.Number or JsonValueKind.Null);

        // Ni el owner ni otro miembro la ven.
        foreach (var other in new[] { owner, tenants.A.Member(OrgRole.Member) })
        {
            using var http = Client(tenants, other);
            using var list = await OwnershipTransferApi.MyTransfersAsync(http);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal("[]", await list.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    [IdorCase("POST", OwnershipTransferApi.AcceptTemplate)]
    [IdorCase("POST", OwnershipTransferApi.DeclineTemplate)]
    public async Task PLAN0018_CA89_NoDestino_AcceptYDeclineDelOwnerOtroMiembroOtraOrganizacionOIdInexistente_Responden404MismoCuerpoSinVerificar()
    {
        var (tenants, admin) = await SeedAsync();
        var owner = tenants.A.Owner;
        using var ownerHttp = Client(tenants, owner);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var calls = _spy.Calls;
        var rowsBefore = await OrganizationDeletionApi.RowCountsAsync(_host.Services);

        using var memberHttp  = Client(tenants, tenants.A.Member(OrgRole.Member));
        using var foreignHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.B);
        using var adminHttp   = Client(tenants, admin);

        var bodies = new List<string>();
        foreach (var (http, id) in new[]
                 {
                     (ownerHttp, transferId), (memberHttp, transferId), (foreignHttp, transferId), (adminHttp, Guid.NewGuid()),
                 })
        {
            // Con la contraseña correcta de cada uno: no se llega a verificarla.
            using (var accept = await OwnershipTransferApi.AcceptAsync(http, id, http == ownerHttp ? OwnerPassword : MemberPassword))
            {
                Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
                bodies.Add(await accept.Content.ReadAsStringAsync());
            }
            using (var decline = await OwnershipTransferApi.DeclineAsync(http, id))
            {
                Assert.Equal(HttpStatusCode.NotFound, decline.StatusCode);
                bodies.Add(await decline.Content.ReadAsStringAsync());
            }
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Equal(calls, _spy.Calls);
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
        Assert.Equal(owner.UserId, (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
        Assert.Equal(rowsBefore, await OrganizationDeletionApi.RowCountsAsync(_host.Services));
    }

    [Fact]
    public async Task PLAN0018_CA89_Vencida_AcceptResponde404SinCambiosNoApareceEnElListadoYOPuedeProponerDeNuevo()
    {
        var (tenants, admin) = await SeedAsync();
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var calls = _spy.Calls;

        _time.Advance(OrgSecurity.OwnershipTransferLifetime + TimeSpan.FromSeconds(1));

        using (var list = await OwnershipTransferApi.MyTransfersAsync(adminHttp))
            Assert.Equal("[]", await list.Content.ReadAsStringAsync());
        using (var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword))
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal(calls, _spy.Calls);
        Assert.Equal(tenants.A.Owner.UserId,
            (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
        // Al detectarse vencida pasa a cancelled.
        Assert.Equal(OwnershipTransferStatus.Cancelled, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);

        var again = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        Assert.NotEqual(transferId, again);
    }

    // ── CA-89: PREVIEW_STALE y TRANSFER_INVALID ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA89_PreviewStale_SeAgregaUnMiembroTrasLaPropuesta_Accept409YLaPropuestaQuedaCancelled()
    {
        // Cinco miembros vigentes: justo el límite de pro, no lo supera. El sexto cambia exceededLimits del aviso.
        var (tenants, admin) = await SeedAsync(a => a.WithMember(OrgRole.Member).WithMember(OrgRole.Member));
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        await OwnershipTransferApi.AddMemberAsync(
            _host.Services, tenants.A.OrganizationId, tenants.B.Owner, OrgRole.Member, tenants.A.Owner.UserId);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);

        using var response = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);

        await OwnershipTransferApi.AssertConflictAsync(response, ProposeOwnershipTransferCommandHandler.PreviewStaleCode);
        await AssertUnchangedAsync(tenants, roles);
        Assert.Equal(OwnershipTransferStatus.Cancelled, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
    }

    [Fact]
    public async Task PLAN0018_CA89_PreviewStale_ElPlanPropioDelDestinoPasaAStudio_Accept409YLaPropuestaQuedaCancelled()
    {
        var (tenants, admin) = await SeedAsync();
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        await OwnershipTransferApi.SeedOwnSubscriptionAsync(_host.Services, admin, tenants.A.Organization, Plan.Studio);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);

        using var response = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);

        await OwnershipTransferApi.AssertConflictAsync(response, ProposeOwnershipTransferCommandHandler.PreviewStaleCode);
        await AssertUnchangedAsync(tenants, roles);
        Assert.Equal(OwnershipTransferStatus.Cancelled, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);

        // Cancelada: el owner debe proponer de nuevo; aceptar otra vez responde 404.
        using var again = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task PLAN0018_CA89_DestinoDadoDeBajaUOrganizacionEliminada_LaPropuestaQuedaCanceladaYAcceptResponde404SinCambios()
    {
        // Baja del destino por el owner (RF-83 h).
        var (removed, removedAdmin) = await SeedAsync();
        using (var ownerHttp = Client(removed, removed.A.Owner))
        {
            var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, removedAdmin.UserId, OwnerPassword);
            using (var removal = await MemberManagementApi.RemoveAsync(ownerHttp, removedAdmin.UserId))
                Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
            using var adminHttp = Client(removed, removedAdmin);
            using var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
            Assert.Equal(removed.A.Owner.UserId,
                (await OrganizationDeletionApi.OrganizationAsync(_host.Services, removed.A.OrganizationId)).OwnerId);
        }

        // Eliminación de la organización (RF-65 b).
        var (deleted, deletedAdmin) = await SeedAsync();
        using (var ownerHttp = Client(deleted, deleted.A.Owner))
        {
            var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, deletedAdmin.UserId, OwnerPassword);
            using (var delete = await OrganizationDeletionApi.DeleteAsync(ownerHttp, deleted.A.Organization.Name, OwnerPassword))
                Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            using var adminHttp = Client(deleted, deletedAdmin);
            using var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
            var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, deleted.A.OrganizationId);
            Assert.Equal(deleted.A.Owner.UserId, organization.OwnerId);
            Assert.NotNull(organization.DeletedAt);
            Assert.Equal(OwnershipTransferStatus.Cancelled, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
        }
    }

    [Theory]
    [InlineData("owner-alterado")]
    [InlineData("eliminada-sin-cancelar")]
    [InlineData("destino-sin-membresia")]
    [InlineData("destino-sin-verificar")]
    public async Task PLAN0018_CA89_TransferInvalid_CambiosEnLaBaseSinCancelarLaPropuesta_Responde409SinCambios(string change)
    {
        var (tenants, admin) = await SeedAsync();
        var member = tenants.A.Member(OrgRole.Member);
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        await AlterAsync(tenants, admin, member, change);
        var organizationBefore = await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId);
        var rolesBefore = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);

        using var response = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);

        await OwnershipTransferApi.AssertConflictAsync(response, OwnershipTransferTarget.InvalidCode);
        var organizationAfter = await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal((organizationBefore.OwnerId, organizationBefore.ConcurrencyStamp, organizationBefore.DeletedAt),
            (organizationAfter.OwnerId, organizationAfter.ConcurrencyStamp, organizationAfter.DeletedAt));
        Assert.Equal(rolesBefore, await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
    }

    // ── CA-114: credencial del destino ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA114_Aceptar_ContrasenaIncorrectaSinCredencialConLaDelOwnerOConElAvisoCambiado_403O422SinCambios()
    {
        var (tenants, admin) = await SeedAsync(a => a.WithMember(OrgRole.Member).WithMember(OrgRole.Member));
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);

        using (var wrong = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, WrongPassword))
        {
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(wrong);
            Assert.DoesNotContain(WrongPassword, await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var none = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, none.StatusCode);
            Assert.Contains(ReauthCredential.FieldName, await none.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var both = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword, "google-trampa"))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, both.StatusCode);
        using (var ownersPassword = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, OwnerPassword))
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(ownersPassword);

        // Con el aviso ya cambiado (un sexto miembro), la credencial va primero: 403 y no 409.
        await OwnershipTransferApi.AddMemberAsync(
            _host.Services, tenants.A.OrganizationId, tenants.B.Owner, OrgRole.Member, tenants.A.Owner.UserId);
        using (var staleAndWrong = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, WrongPassword))
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(staleAndWrong);

        roles[tenants.B.Owner.UserId] = OrgRole.Member;
        await AssertUnchangedAsync(tenants, roles);
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
    }

    [Fact]
    public async Task PLAN0018_CA114_ListadoYRechazo_RespondenSinCredencial()
    {
        var (tenants, admin) = await SeedAsync();
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var calls = _spy.Calls;

        using (var list = await OwnershipTransferApi.MyTransfersAsync(adminHttp))
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var decline = await OwnershipTransferApi.DeclineAsync(adminHttp, transferId))
            Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);

        Assert.Equal(calls, _spy.Calls);
    }

    // ── CA-125: límite del destino al aceptar ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA125_Aceptar_DestinoFreeQueCreoOtraOrganizacionTrasLaPropuesta_Responde422QuotaExceededSinCambios()
    {
        // El destino no tiene suscripción propia: su plan es free (max_owned_orgs = 1) y no es owner de nada.
        var (tenants, admin) = await SeedAsync(ownPlan: null);
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        await OwnershipTransferApi.SeedOwnedOrganizationAsync(_host.Services, admin, LegalType.Empresa);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);

        using var response = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("QUOTA_EXCEEDED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(PlanLimits.FeatureKeys.MaxOwnedOrgs, body.RootElement.GetProperty("featureKey").GetString());
        await AssertUnchangedAsync(tenants, roles);
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
    }

    // ── CA-55: roles tras la transferencia ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA55_TrasAceptar_ElAnteriorOwnerEsAdminNoTocaAlNuevoOwnerYElNuevoOwnerLoHaceMember()
    {
        var (tenants, admin) = await SeedAsync();
        var owner = tenants.A.Owner;
        using var ownerHttp = Client(tenants, owner);
        using var adminHttp = Client(tenants, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        using (var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword))
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);

        // Con el mismo access token, el owner anterior ya es admin: escribe la configuración pero no toca al nuevo owner.
        using (var settings = await MemberManagementApi.PatchSettingsAsync(ownerHttp))
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        using (var demote = await MemberManagementApi.ChangeRoleAsync(ownerHttp, admin.UserId, OrgRole.Member))
            await ForbiddenAssert.IsAccessDeniedAsync(demote);
        using (var remove = await MemberManagementApi.RemoveAsync(ownerHttp, admin.UserId))
            await ForbiddenAssert.IsAccessDeniedAsync(remove);

        using (var demoted = await MemberManagementApi.ChangeRoleAsync(adminHttp, owner.UserId, OrgRole.Member))
            Assert.Equal(HttpStatusCode.NoContent, demoted.StatusCode);
        Assert.Equal(OrgRole.Member, (await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId))[owner.UserId]);
        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    // ── CA-20: rutas del destino desde otra organización activa ─────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA20_DestinoConOtraOrganizacionActivaOSinNinguna_ListaYAceptaConElMismoAviso()
    {
        // Siete miembros vigentes: el aviso incluye max_org_members superado, así el previewHash depende de contar los
        // miembros de A aunque la organización activa del destino sea otra.
        var (tenants, admin) = await SeedAsync(a => a.WithMember(OrgRole.Member).WithMember(OrgRole.Member)
            .WithMember(OrgRole.Member).WithMember(OrgRole.Member));
        using var ownerHttp = Client(tenants, tenants.A.Owner);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);

        // A también es member de B y tiene B activa: el filtro de tenant no le oculta la propuesta ni cambia el aviso.
        await OwnershipTransferApi.AddMemberAsync(_host.Services, tenants.B.OrganizationId, admin, OrgRole.Member, tenants.B.Owner.UserId);
        using var inB = AuthSessionApi.Bearer(_host, _factory.Tokens.ForUser(admin, tenants.B.Organization, OrgRole.Member));
        using var withoutOrganization = AuthSessionApi.Bearer(_host,
            OrganizationRestoreApi.TokenWithoutOrganization(_host.Services, admin));

        foreach (var http in new[] { inB, withoutOrganization })
        {
            using var list = await OwnershipTransferApi.MyTransfersAsync(http);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var item = Assert.Single(body.RootElement.EnumerateArray());
            Assert.Equal(transferId, item.GetProperty("id").GetGuid());
            var members = Assert.Single(item.GetProperty("preview").GetProperty("exceededLimits").EnumerateArray(),
                l => l.GetProperty("featureKey").GetString() == PlanLimits.FeatureKeys.MaxOrgMembers);
            Assert.Equal(7, members.GetProperty("usage").GetInt32());
        }

        using var accept = await OwnershipTransferApi.AcceptAsync(inB, transferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        Assert.Equal(admin.UserId, (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
        await MembershipInvariant.AssertHoldsAsync(_host.Services, tenants);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Escenario de CA-89: A empresa + studio (owner O con contraseña), admin y member verificados con contraseña. El admin
    // tiene plan propio pro (o ninguno: free) y no es owner de nada.
    private async Task<(TenantPair Tenants, User Admin)> SeedAsync(
        Action<TenantOptions>? configureA = null, string? ownPlan = Plan.Pro)
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword,
            configureA: a =>
            {
                a.WithPlan(Plan.Studio);
                configureA?.Invoke(a);
            });
        var admin = tenants.A.Member(OrgRole.Admin);
        if (ownPlan is not null)
            await OwnershipTransferApi.SeedOwnSubscriptionAsync(_host.Services, admin, tenants.A.Organization, ownPlan);
        return (tenants, admin);
    }

    private HttpClient Client(TenantPair tenants, User user)
        => OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, user);

    // Sin cambios: el owner sigue siendo O, la organización vigente y los roles los mismos.
    private async Task AssertUnchangedAsync(TenantPair tenants, Dictionary<Guid, string> roles)
    {
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal(tenants.A.Owner.UserId, organization.OwnerId);
        Assert.Null(organization.DeletedAt);
        Assert.Equal(roles, await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId));
        Assert.DoesNotContain(await OwnershipTransferApi.ActivityAsync(_host.Services, tenants.A.OrganizationId),
            a => a.Action == AcceptOwnershipTransferCommandHandler.LogAction);
    }

    // Cambios hechos directo en la base, sin pasar por los comandos que cancelarían la propuesta (dobles de RF-83 c).
    private async Task AlterAsync(TenantPair tenants, User admin, User member, string change)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        switch (change)
        {
            case "owner-alterado":
                var organization = await db.Organizations.SingleAsync(o => o.OrganizationId == tenants.A.OrganizationId);
                db.Entry(organization).Property(o => o.OwnerId).CurrentValue = member.UserId;
                break;
            case "eliminada-sin-cancelar":
                var deleted = await db.Organizations.SingleAsync(o => o.OrganizationId == tenants.A.OrganizationId);
                deleted.MarkDeleted(tenants.A.Owner.UserId, DateTime.UtcNow);
                break;
            case "destino-sin-membresia":
                var membership = await db.OrganizationMembers.IgnoreQueryFilters()
                    .SingleAsync(m => m.OrganizationId == tenants.A.OrganizationId && m.UserId == admin.UserId && m.DeletedAt == null);
                membership.SoftDelete();
                break;
            case "destino-sin-verificar":
                var user = await db.Users.SingleAsync(u => u.UserId == admin.UserId);
                db.Entry(user).Property(u => u.EmailVerifiedAt).CurrentValue = null;
                break;
        }
        await db.SaveChangesAsync();
    }
}

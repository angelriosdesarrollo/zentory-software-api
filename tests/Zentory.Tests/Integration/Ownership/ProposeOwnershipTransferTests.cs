using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zentory.API.Authorization;
using Zentory.API.Controllers.V1;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 T30b sobre el host real: POST /organization/transfer-ownership con contraseña. CA-T30B-1 (parte de CA-114):
// orden de RF-81 e (policy de owner → credencial → destino, límite y acuse) y sin efecto en ningún fallo; CA-123: los dos
// correos de la propuesta sin token y con los valores de usuario codificados. Proponer no cambia OwnerId, roles, refresh
// tokens, plan ni Subscriptions (D-33). El host deriva de la factory (verificador espía y logs capturados) y comparte su
// base InMemory.
public sealed class ProposeOwnershipTransferTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Transferir-2026!";
    private const string MemberPassword = "Miembro-Transferir-2026!";
    private const string WrongPassword  = "Incorrecta-Transferir-Trampa-2026!";
    private const string StaleHash      = "0000000000000000000000000000000000000000000000000000000000000000";

    private const string HostileOrganizationName = "<a href=\"https://x\">y</a>";
    private const string HostilePersonName       = "<script>";

    private readonly ZentoryApiFactory              _factory;
    private readonly ReauthVerifierSpy              _spy  = new();
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public ProposeOwnershipTransferTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureTestServices(_spy.Install));
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── CA-T30B-1 (parte de CA-114): proponer ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA114_Proponer_ContrasenaIncorrecta_Responde403ReauthenticationFailedSinEfecto()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        var hash = await OwnershipTransferApi.PreviewHashAsync(http, admin.UserId);
        var before = await SnapshotAsync(tenants);

        using var response = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, hash, WrongPassword);

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(response);
        Assert.DoesNotContain(WrongPassword, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertNoEffectAsync(tenants, before);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PLAN0018_CA114_Proponer_SinCredencialOConLasDos_Responde422SinEfecto(bool withPassword, bool withGoogle)
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        var hash = await OwnershipTransferApi.PreviewHashAsync(http, admin.UserId);
        var before = await SnapshotAsync(tenants);

        using var response = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, hash,
            withPassword ? OwnerPassword : null, withGoogle ? "google-id-token-trampa" : null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(ReauthCredential.FieldName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(OwnerPassword, text, StringComparison.Ordinal);
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_ContrasenaIncorrectaYAcuseVencido_Responde403YNo409()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        var before = await SnapshotAsync(tenants);

        using var response = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, StaleHash, WrongPassword);

        await OwnershipTransferApi.AssertReauthenticationFailedAsync(response);
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_LlamadoPorElAdminConSuContrasenaCorrecta_Responde403AccessDeniedSinVerificar()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        var calls = _spy.Calls;
        var before = await SnapshotAsync(tenants);
        using var http = Client(tenants, tenants.A, admin);

        using var response = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, StaleHash, MemberPassword);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(calls, _spy.Calls);
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_OwnerConContrasenaYAcuseVigente_Responde202SinCambiarOwnerRolesNiPlan()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        var hash = await OwnershipTransferApi.PreviewHashAsync(http, admin.UserId);
        var rolesBefore = await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId);
        var activityBefore = await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId);
        var startedAt = DateTime.UtcNow;

        using var response = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, hash, OwnerPassword);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var transferId = body.RootElement.GetProperty("transferId").GetGuid();
        var expiresAt  = body.RootElement.GetProperty("expiresAt").GetDateTime();

        var transfer = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(transferId, transfer.Id);
        Assert.Equal(OwnershipTransferStatus.Pending, transfer.Status);
        Assert.Equal((tenants.A.Owner.UserId, admin.UserId), (transfer.FromUserId, transfer.ToUserId));
        Assert.Equal(hash, transfer.PreviewHash);
        Assert.Null(transfer.ResolvedAt);
        Assert.InRange(transfer.ExpiresAt, startedAt + OrgSecurity.OwnershipTransferLifetime - TimeSpan.FromMinutes(1),
            DateTime.UtcNow + OrgSecurity.OwnershipTransferLifetime);
        Assert.Equal(transfer.ExpiresAt, expiresAt.ToUniversalTime());

        // Nada cambia hasta la aceptación (T30c): owner, roles y plan efectivo.
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal(tenants.A.Owner.UserId, organization.OwnerId);
        Assert.Equal(rolesBefore, await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(activityBefore + 1, await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_AcuseDistintoOAusenteConContrasenaCorrecta_Responde409PreviewStaleSinEfecto()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        var before = await SnapshotAsync(tenants);

        using (var stale = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, StaleHash, OwnerPassword))
            await OwnershipTransferApi.AssertConflictAsync(stale, ProposeOwnershipTransferCommandHandler.PreviewStaleCode);
        using (var missing = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, null, OwnerPassword))
            await OwnershipTransferApi.AssertConflictAsync(missing, ProposeOwnershipTransferCommandHandler.PreviewStaleCode);

        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA89_Proponer_HaciaUnFreeQueYaEsOwner_Responde422QuotaExceededSinPropuestaNiConteoReal()
    {
        // Deuda de TK-0069 (RF-64 b; SR-0018.46): el destino free es owner de dos organizaciones vigentes (B y otra), con
        // límite 1. El 422 informa used = limit, nunca el conteo real.
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword,
            configureB: b => b.WithPlan(Plan.Free));
        var freeOwner = tenants.B.Owner;
        await OwnershipTransferApi.SeedOwnedOrganizationAsync(_host.Services, freeOwner, tenants.B.LegalType);
        await OwnershipTransferApi.AddMemberAsync(
            _host.Services, tenants.A.OrganizationId, freeOwner, OrgRole.Member, tenants.A.Owner.UserId);
        await OwnershipTransferApi.MarkEmailVerifiedAsync(_host.Services, freeOwner);
        using var http = Client(tenants, tenants.A);
        var hash = await OwnershipTransferApi.PreviewHashAsync(http, freeOwner.UserId);
        var before = await SnapshotAsync(tenants);

        using var response = await OwnershipTransferApi.ProposeAsync(http, freeOwner.UserId, hash, OwnerPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("QUOTA_EXCEEDED", root.GetProperty("code").GetString());
        Assert.Equal(PlanLimits.FeatureKeys.MaxOwnedOrgs, root.GetProperty("featureKey").GetString());
        Assert.Equal(OwnershipTransferQuotaExceededException.TransferMessage, root.GetProperty("message").GetString());
        var detail = Assert.Single(root.GetProperty("details").EnumerateArray());
        Assert.Equal(1, detail.GetProperty("limit").GetInt32());
        Assert.Equal(detail.GetProperty("limit").GetInt32(), detail.GetProperty("used").GetInt32());
        Assert.DoesNotContain(detail.EnumerateObject(), p => p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() == 2);
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA114_PreviewYCancelar_RespondenSinCredencial()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);
        var calls = _spy.Calls;

        using (var preview = await OwnershipTransferApi.PreviewAsync(http, admin.UserId))
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using (var cancel = await OwnershipTransferApi.CancelAsync(http))
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        Assert.Equal(calls, _spy.Calls);
    }

    [Fact]
    public async Task PLAN0018_CA114_Proponer_LogsCapturados_NoRegistranElCuerpoNiLaCredencial()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);

        (await OwnershipTransferApi.ProposeAsync(http, admin.UserId, StaleHash, WrongPassword)).Dispose();
        (await OwnershipTransferApi.ProposeAsync(http, admin.UserId, StaleHash, OwnerPassword, "google-trampa-cuerpo")).Dispose();

        Assert.NotEmpty(_logs.Entries);
        foreach (var secret in new[] { WrongPassword, OwnerPassword, "google-trampa-cuerpo", StaleHash, "currentPassword" })
            Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA114_RutasDelOwner_DeclaranRequireOrgOwnerYSoloProponerLlevaLosLimites()
    {
        // RF-81 e, f y SR-0018.29: proponer lleva organization-transfer y reauth; el aviso y cancelar no piden credencial.
        var propose = typeof(OrganizationController).GetMethod(nameof(OrganizationController.ProposeOwnershipTransfer))!;
        var preview = typeof(OrganizationController).GetMethod(nameof(OrganizationController.GetTransferOwnershipPreview))!;
        var cancel  = typeof(OrganizationController).GetMethod(nameof(OrganizationController.CancelOwnershipTransfer))!;

        foreach (var action in new[] { propose, preview, cancel })
            Assert.Equal(PolicyNames.RequireOrgOwner, Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.NotNull(propose.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(preview.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(cancel.GetCustomAttribute<HttpDeleteAttribute>());
        Assert.Equal(
            [AuthRateLimitPolicies.OrganizationTransfer, AuthRateLimitPolicies.Reauth],
            propose.GetCustomAttributes<AuthRateLimitAttribute>().Select(a => a.Policy).Order(StringComparer.Ordinal));
        Assert.Empty(preview.GetCustomAttributes<AuthRateLimitAttribute>());
        Assert.Empty(cancel.GetCustomAttributes<AuthRateLimitAttribute>());
    }

    // ── CA-123: correos de la propuesta ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA123_Proponer_EncolaCorreoAlDestinoYAvisoAlOwner_SinTokenNiEnlace()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);

        using (var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId, OwnerPassword))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var transfer = Assert.Single(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        var toTarget = await WaitForEmailAsync(nameof(IEmailService.SendOwnershipTransferProposedEmailAsync), admin.Email);
        var toOwner  = await WaitForEmailAsync(nameof(IEmailService.SendOwnershipTransferProposedOwnerNoticeAsync),
            tenants.A.Owner.Email);

        Assert.Equal(tenants.A.Organization.Name, toTarget.OrganizationName);
        Assert.Equal($"{tenants.A.Owner.FirstName} {tenants.A.Owner.LastName}", toTarget.OtherPartyName);
        Assert.Equal(transfer.ExpiresAt, toTarget.ExpiresAtUtc);
        Assert.Null(toTarget.Url);
        Assert.Equal(tenants.A.Organization.Name, toOwner.OrganizationName);
        Assert.Equal($"{admin.FirstName} {admin.LastName}", toOwner.OtherPartyName);
        Assert.Null(toOwner.Url);
    }

    [Fact]
    public async Task PLAN0018_CA123_Proponer_NombresConHtml_LasPlantillasLosEscapanYNoLlevanTokenNiEnlace()
    {
        var resend = CapturingResend.Create();
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(new ResendEmailService(resend, new ConfigurationBuilder().Build()));
        }));
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        await RenameAsync(tenants.A, HostileOrganizationName, HostilePersonName);
        using var http = OrganizationDeletionApi.ClientFor(host, tenants, tenants.A);

        using (var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId, OwnerPassword))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var capturing = CapturingResend.Of(resend);
        await OrganizationDeletionApi.WaitUntilAsync(() => capturing.Messages.Count >= 2, "La cola no entregó los dos correos.");
        foreach (var message in capturing.Messages.ToArray())
        {
            Assert.Contains("&lt;a href=&quot;https://x&quot;&gt;y&lt;/a&gt;", message.HtmlBody);
            Assert.DoesNotContain(HostileOrganizationName, message.HtmlBody);
            Assert.DoesNotContain(HostilePersonName, message.HtmlBody);
            Assert.DoesNotContain("token", message.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<a ", message.HtmlBody, StringComparison.Ordinal);
            Assert.DoesNotContain("<", message.Subject);
        }
        // El destino ve el nombre del owner codificado.
        Assert.Contains(capturing.Messages, m => m.HtmlBody!.Contains("&lt;script&gt;", StringComparison.Ordinal));
    }

    // ── CA-T30B-2 (parte de CA-127): correo al destino sin token ────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA127_Proponer_ElCorreoAlDestinoNoLlevaTokenYProponerNoEmiteNingunEnlaceDeUnSoloUso()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = Client(tenants, tenants.A);

        using (var response = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId, OwnerPassword))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var email = await WaitForEmailAsync(nameof(IEmailService.SendOwnershipTransferProposedEmailAsync), admin.Email);
        Assert.Null(email.Url);
        Assert.DoesNotContain(Emails.Sent, sent => sent.Url is not null
            && (sent.ToEmail == admin.Email || sent.ToEmail == tenants.A.Owner.Email));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<TenantPair> SeedAsync()
        => OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword);

    private HttpClient Client(TenantPair tenants, SeededTenant tenant, User? user = null)
        => OrganizationDeletionApi.ClientFor(_host, tenants, tenant, user);

    private sealed record Snapshot(IReadOnlyDictionary<string, int> Rows, int Activity, Dictionary<Guid, string> Roles);

    private async Task<Snapshot> SnapshotAsync(TenantPair tenants)
        => new(await OrganizationDeletionApi.RowCountsAsync(_host.Services),
            await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId),
            await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId));

    // Sin efecto: ninguna propuesta, mismo owner, mismos roles, ningún conteo de tabla distinto (refresh tokens incluidos)
    // y ninguna fila nueva de ActivityLog en A; tampoco sale correo de propuesta.
    private async Task AssertNoEffectAsync(TenantPair tenants, Snapshot before)
    {
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(_host.Services, tenants.A.OrganizationId));
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Equal(tenants.A.Owner.UserId, organization.OwnerId);
        Assert.Equal(before.Roles, await OwnershipTransferApi.RolesAsync(_host.Services, tenants.A.OrganizationId));
        Assert.Equal(before.Rows, await OrganizationDeletionApi.RowCountsAsync(_host.Services));
        Assert.Equal(before.Activity, await OwnershipTransferApi.ActivityCountAsync(_host.Services, tenants.A.OrganizationId));
        var recipients = tenants.A.Members.Select(m => m.User.Email).Append(tenants.A.Owner.Email).ToHashSet();
        Assert.DoesNotContain(Emails.Sent, sent => recipients.Contains(sent.ToEmail)
            && sent.Kind.StartsWith("SendOwnershipTransfer", StringComparison.Ordinal));
    }

    private async Task<FakeEmailService.SentEmail> WaitForEmailAsync(string kind, string toEmail)
    {
        FakeEmailService.SentEmail? Match()
            => Emails.Sent.FirstOrDefault(sent => sent.Kind == kind && sent.ToEmail == toEmail);
        await OrganizationDeletionApi.WaitUntilAsync(() => Match() is not null, $"La cola no entregó {kind}.");
        return Match()!;
    }

    private async Task RenameAsync(SeededTenant tenant, string organizationName, string ownerFirstName)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Zentory.Infrastructure.Persistence.ZentoryDbContext>();
        var organization = await db.Organizations.FindAsync(tenant.OrganizationId);
        var owner = await db.Users.FindAsync(tenant.Owner.UserId);
        db.Entry(organization!).Property(o => o.Name).CurrentValue = organizationName;
        db.Entry(owner!).Property(u => u.FirstName).CurrentValue = ownerFirstName;
        db.Entry(owner!).Property(u => u.LastName).CurrentValue = string.Empty;
        await db.SaveChangesAsync();
    }
}

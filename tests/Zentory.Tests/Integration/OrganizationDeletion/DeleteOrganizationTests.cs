using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Zentory.API.Authorization;
using Zentory.API.Controllers.V1;
using Zentory.API.Extensions;
using Zentory.Application.Common.Security;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.OrganizationDeletion;

// PLAN-0018 CA-102 (RF-65 a, b) y CA-109 (RF-81 a, b, d, e; RF-65 a2) sobre el host real: DELETE /organization con
// contraseña. Orden de RF-81 e: la policy de owner va antes que la credencial (un admin con su contraseña correcta no
// llega al verificador, que es un espía sobre el real), la credencial antes que el nombre, y sin efecto en ningún fallo.
// El host deriva de la factory (verificador espía y logs capturados) y comparte su base InMemory.
public sealed class DeleteOrganizationTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Correcta-2026!";
    private const string MemberPassword = "Miembro-Correcta-2026!";
    private const string WrongPassword  = "Incorrecta-Trampa-2026!";
    private const string GoogleTokenTrap = "google-id-token-trampa-0123456789";

    private readonly ZentoryApiFactory              _factory;
    private readonly ReauthVerifierSpy              _spy  = new();
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public DeleteOrganizationTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureTestServices(_spy.Install));
    }

    public void Dispose() => _host.Dispose();

    private FakeEmailService Emails => _host.Services.GetRequiredService<FakeEmailService>();

    // ── CA-102 ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA102_DeleteOrganization_DelAdminODelMember_Responde403AccessDeniedSinEfecto(string role)
    {
        var tenants = await SeedAsync();
        var before = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        var calls = _spy.Calls;
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, tenants.A.Member(role));

        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, MemberPassword);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        await AssertNoEffectAsync(tenants, before);
        // CA-109: el verificador de credenciales no se invoca.
        Assert.Equal(calls, _spy.Calls);
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task PLAN0018_CA102_DeleteOrganization_OwnerConConfirmNameDistinto_Responde422SinEfecto(string? variant)
    {
        var tenants = await SeedAsync();
        var name = tenants.A.Organization.Name;
        var confirmName = variant switch
        {
            "upper" => name.ToUpperInvariant(),
            "lower" => name.ToLowerInvariant(),
            _       => variant,
        };
        Assert.NotEqual(name, confirmName);
        var before = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        using var response = await OrganizationDeletionApi.DeleteAsync(http, confirmName, OwnerPassword);

        await AssertValidationErrorAsync(response, DeleteOrganizationCommandHandler.ConfirmNameField);
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA102_DeleteOrganization_OwnerConNombreExactoYContrasena_Responde204SinBorrarFilasNiArchivos()
    {
        var tenants = await SeedAsync();
        var organizationId = tenants.A.OrganizationId;
        var storage = _host.Services.GetRequiredService<FakeStorageService>();
        var fileKey = $"{organizationId}/documents/{Guid.NewGuid():N}.pdf";
        await storage.UploadAsync(fileKey, new MemoryStream([1, 2, 3]), "application/pdf");
        var rowsBefore  = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        var filesBefore = storage.Objects.Keys.Order(StringComparer.Ordinal).ToList();
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        // Comparación exacta tras quitar los espacios de los extremos (RF-65 a).
        using var response = await OrganizationDeletionApi.DeleteAsync(http, $"  {tenants.A.Organization.Name} ", OwnerPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, organizationId);
        Assert.NotNull(organization.DeletedAt);
        Assert.Equal(tenants.A.Owner.UserId, organization.DeletedBy);
        Assert.Null(organization.PurgeStartedAt);
        Assert.Equal(rowsBefore, await OrganizationDeletionApi.RowCountsAsync(_host.Services));
        Assert.Equal(filesBefore, storage.Objects.Keys.Order(StringComparer.Ordinal).ToList());
        Assert.Contains(fileKey, storage.Objects.Keys);
        await AssertDeletionAuditedAsync(organizationId, tenants.A.Owner.UserId);
    }

    // ── CA-109 ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA109_DeleteOrganization_ContrasenaIncorrecta_Responde403ReauthenticationFailedSinEfecto()
    {
        // De extremo a extremo con credencial incorrecta (pedido por la verificación de TK-0037).
        var tenants = await SeedAsync();
        var before = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, WrongPassword);

        await AssertReauthenticationFailedAsync(response);
        Assert.DoesNotContain(WrongPassword, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await AssertNoEffectAsync(tenants, before);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task PLAN0018_CA109_DeleteOrganization_SinCredencialOConLasDos_Responde422SinEcoNiEfecto(
        bool withPassword, bool withGoogle)
    {
        var tenants = await SeedAsync();
        var before = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name,
            withPassword ? OwnerPassword : null, withGoogle ? GoogleTokenTrap : null);

        var text = await AssertValidationErrorAsync(response, ReauthCredential.FieldName);
        // SR-0018.30 d: el 422 no repite el valor enviado.
        Assert.DoesNotContain(OwnerPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(GoogleTokenTrap, text, StringComparison.Ordinal);
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA109_DeleteOrganization_AdminConSuContrasenaCorrecta_Responde403SinInvocarAlVerificador()
    {
        var tenants = await SeedAsync();
        var calls = _spy.Calls;
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, tenants.A.Member(OrgRole.Admin));

        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, MemberPassword);

        await ForbiddenAssert.IsAccessDeniedAsync(response);
        Assert.Equal(calls, _spy.Calls);

        // Control del espía: la misma petición del owner sí pasa por el verificador.
        using var owner = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        using var ownerResponse = await OrganizationDeletionApi.DeleteAsync(owner, tenants.A.Organization.Name, WrongPassword);
        await AssertReauthenticationFailedAsync(ownerResponse);
        Assert.Equal(calls + 1, _spy.Calls);
    }

    [Fact]
    public async Task PLAN0018_CA109_DeleteOrganization_ContrasenaCorrectaYNombreDistinto_Responde422YLuegoConElNombre204()
    {
        var tenants = await SeedAsync();
        var before = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        using (var wrongName = await OrganizationDeletionApi.DeleteAsync(http, "Otra organización", OwnerPassword))
        {
            await AssertValidationErrorAsync(wrongName, DeleteOrganizationCommandHandler.ConfirmNameField);
            await AssertNoEffectAsync(tenants, before);
        }

        using var response = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull((await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).DeletedAt);
    }

    [Fact]
    public async Task PLAN0018_CA109_DeleteOrganization_ContrasenaIncorrectaYNombreDistinto_Responde403PorqueVerificaAntesDelNombre()
    {
        // RF-81 e: la credencial se verifica antes que confirmName. Con los dos mal, gana el 403 y el fallo queda
        // registrado (AuditLog REAUTHFAIL y conteo para el aviso del tercer fallo).
        var tenants = await SeedAsync();
        var owner = tenants.A.Owner;
        var before = await OrganizationDeletionApi.RowCountsAsync(_host.Services);
        var calls = _spy.Calls;
        var auditBefore = await ReauthFailuresAsync(owner.UserId);
        var limiter = _host.Services.GetRequiredService<AuthAttemptLimiter>();
        var subject = new AuthAttemptSubject(owner.UserId.ToString(), null, null);
        // RecordFailure devuelve el conteo de la ventana incluyendo el que registra: sirve de sonda antes y después.
        var failuresBefore = limiter.RecordFailure(AuthRateLimitPolicies.Reauth, subject);
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        using var response = await OrganizationDeletionApi.DeleteAsync(http, "Otra organización", WrongPassword);

        await AssertReauthenticationFailedAsync(response);
        Assert.Equal(calls + 1, _spy.Calls);
        Assert.Equal(auditBefore + 1, await ReauthFailuresAsync(owner.UserId));
        Assert.Equal(failuresBefore + 2, limiter.RecordFailure(AuthRateLimitPolicies.Reauth, subject));
        await AssertNoEffectAsync(tenants, before);
    }

    [Fact]
    public async Task PLAN0018_CA109_DeleteOrganization_LogsCapturados_NoRegistranElCuerpoNiLaCredencial()
    {
        var tenants = await SeedAsync();
        using var http = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);

        (await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, WrongPassword)).Dispose();
        (await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, OwnerPassword, GoogleTokenTrap)).Dispose();
        (await OrganizationDeletionApi.DeleteAsync(http, "nombre-trampa-del-cuerpo", OwnerPassword)).Dispose();

        Assert.NotEmpty(_logs.Entries);
        foreach (var secret in new[] { WrongPassword, OwnerPassword, GoogleTokenTrap, "nombre-trampa-del-cuerpo", "currentPassword" })
            Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void PLAN0018_CA111_DeleteOrganization_DeclaraRequireOrgOwnerYLosLimitesOrganizationDeleteYReauth()
    {
        // RF-81 e, f y SR-0018.29: la policy de owner y los dos límites, cada uno con su contador.
        var action = typeof(OrganizationController).GetMethod(nameof(OrganizationController.Delete))!;

        Assert.NotNull(action.GetCustomAttribute<HttpDeleteAttribute>());
        Assert.Equal(PolicyNames.RequireOrgOwner, Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal(
            [AuthRateLimitPolicies.OrganizationDelete, AuthRateLimitPolicies.Reauth],
            action.GetCustomAttributes<AuthRateLimitAttribute>().Select(a => a.Policy).Order(StringComparer.Ordinal));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<TenantPair> SeedAsync()
        => new TenantBuilder(_factory)
            .ForA(a => a.WithMember(OrgRole.Admin).WithMember(OrgRole.Member)
                .WithOwnerPassword(OwnerPassword).WithMemberPassword(MemberPassword))
            .BuildAsync();

    // Sin efecto: DeletedAt sigue en null, ninguna tabla cambió de conteo y no salió ningún correo a la organización.
    private async Task AssertNoEffectAsync(TenantPair tenants, IReadOnlyDictionary<string, int> before)
    {
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId);
        Assert.Null(organization.DeletedAt);
        Assert.Null(organization.DeletedBy);
        Assert.Equal(before, await OrganizationDeletionApi.RowCountsAsync(_host.Services));

        var recipients = tenants.A.Members.Select(m => m.User.Email).Append(tenants.A.Owner.Email).ToHashSet();
        Assert.DoesNotContain(Emails.Sent, email => recipients.Contains(email.ToEmail));
    }

    private static async Task AssertReauthenticationFailedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("forbidden", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(ReauthenticationFailedException.Code, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal("validation_error", body.RootElement.GetProperty("error").GetString());
        Assert.Contains(body.RootElement.GetProperty("details").EnumerateArray(),
            detail => detail.GetProperty("field").GetString() == field);
        return text;
    }

    private async Task<int> ReauthFailuresAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.AuditLogs.CountAsync(a => a.UserId == userId && a.Action == ReauthenticationVerifier.AuditAction);
    }

    // RF-65 b: la eliminación deja su fila de AuditLog (y de ActivityLog en una organización empresa).
    private async Task AssertDeletionAuditedAsync(Guid organizationId, Guid ownerId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();

        var audit = await db.AuditLogs.SingleAsync(a =>
            a.RecordId == organizationId && a.Action == DeleteOrganizationCommandHandler.AuditAction);
        Assert.Equal(DeleteOrganizationCommandHandler.AuditTable, audit.TableName);
        Assert.Equal(organizationId, audit.OrganizationId);
        Assert.Equal(ownerId, audit.UserId);
        Assert.DoesNotContain(OwnerPassword, audit.NewValues ?? string.Empty, StringComparison.Ordinal);

        Assert.True(await db.ActivityLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.OrganizationId == organizationId && a.EntityId == organizationId));
    }
}

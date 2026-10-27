using System.Net;
using Zentory.API.Authorization;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-106, punto de POST /organization/transfer-ownership (SR-0018.29), con los límites de organización en sus
// valores por defecto y reauth:{sub} subido: el 6.º intento de la hora responde 429 sin efecto, y el contador es por usuario.
public sealed class OwnershipTransferHourlyLimitTests(OrganizationRateLimitApiFactory factory)
    : IClassFixture<OrganizationRateLimitApiFactory>
{
    private const string OwnerPassword  = "Owner-Limite-Transferir-2026!";
    private const string MemberPassword = "Miembro-Limite-Transferir-2026!";
    private const int    Attempts       = 5;

    [Fact]
    public async Task PLAN0018_CA106_Transferir_SuperaCincoPorHoraPorUsuario_ElSextoResponde429SinPropuesta()
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword,
            configureB: b => b.WithMember(OrgRole.Admin).WithOwnerPassword(OwnerPassword));
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A);

        // Acuse distinto: 409 sin efecto, pero el intento cuenta para organization-transfer.
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var rejected = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, new string('0', 64), OwnerPassword);
            await OwnershipTransferApi.AssertConflictAsync(rejected, ProposeOwnershipTransferCommandHandler.PreviewStaleCode);
        }

        using (var limited = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(http, admin.UserId, OwnerPassword))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Contains(AuthRateLimitAttribute.RateLimitedCode, await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.True(limited.Headers.Contains("Retry-After"));
        }
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));

        // El contador es por usuario: el owner de B propone la suya.
        using var other = tenants.ClientFor(tenants.B);
        using var allowed = await OwnershipTransferApi.ProposeWithCurrentPreviewAsync(
            other, tenants.B.Member(OrgRole.Admin).UserId, OwnerPassword);
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }
}

// PLAN-0018 CA-T30B-3 (parte de CA-111; RF-81 f) con reauth:{sub} en su valor por defecto (5 cada 15 min) y los demás
// subidos: proponer con credencial suma en el mismo contador que DELETE /organization.
public sealed class OwnershipTransferReauthLimitTests(ReauthRateLimitApiFactory factory)
    : IClassFixture<ReauthRateLimitApiFactory>
{
    private const string OwnerPassword  = "Owner-Reauth-Transferir-2026!";
    private const string MemberPassword = "Miembro-Reauth-Transferir-2026!";
    private const string WrongPassword  = "Incorrecta-Reauth-Transferir-2026!";

    [Fact]
    public async Task PLAN0018_CA111_TresEliminarYDosProponerConContrasenaIncorrecta_ElSextoCorrectoResponde429ConRetryAfter()
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword);
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A);
        var hash = await OwnershipTransferApi.PreviewHashAsync(http, admin.UserId);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var delete = await OrganizationDeletionApi.DeleteAsync(http, tenants.A.Organization.Name, WrongPassword);
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(delete);
        }
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var propose = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, hash, WrongPassword);
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(propose);
        }

        using var limited = await OwnershipTransferApi.ProposeAsync(http, admin.UserId, hash, OwnerPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains(AuthRateLimitAttribute.RateLimitedCode, await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After"))) > 0);
        Assert.Empty(await OwnershipTransferApi.TransfersAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Null((await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).DeletedAt);
    }
}

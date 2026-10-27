using System.Net;
using System.Text.Json;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 T30c sobre el host real: el destino rechaza la propuesta (RF-83 f). CA-T30C-1 (parte de CA-127, se cierra en
// T33d): decline 204 → declined sin más cambios y con ActivityLog; un segundo decline o un accept posterior, 404; decline
// del owner o de otro miembro, 404; tras cancelar, accept 404. CA-60: proponer, cancelar, rechazar y aceptar dejan cada
// uno su fila de ActivityLog con actor, objetivo, valor anterior y valor nuevo.
public sealed class DeclineOwnershipTransferTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword  = "Owner-Rechazar-2026!";
    private const string MemberPassword = "Miembro-Rechazar-2026!";

    [Fact]
    public async Task PLAN0018_CA127_Rechazar_DelDestino_Responde204DeclinedSinMasCambiosYConActivityLog_ElSegundoYUnAcceptResponden404()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var ownerHttp = tenants.ClientFor(tenants.A);
        using var adminHttp = tenants.ClientFor(tenants.A, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        var rows  = await OrganizationDeletionApi.RowCountsAsync(factory.Services);
        var roles = await OwnershipTransferApi.RolesAsync(factory.Services, tenants.A.OrganizationId);
        var activity = await OwnershipTransferApi.ActivityCountAsync(factory.Services, tenants.A.OrganizationId);

        using (var decline = await OwnershipTransferApi.DeclineAsync(adminHttp, transferId))
            Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);

        var transfer = await OwnershipTransferApi.TransferAsync(factory.Services, transferId);
        Assert.Equal(OwnershipTransferStatus.Declined, transfer.Status);
        Assert.NotNull(transfer.ResolvedAt);
        Assert.Equal(rows, await OrganizationDeletionApi.RowCountsAsync(factory.Services));
        Assert.Equal(roles, await OwnershipTransferApi.RolesAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Equal(tenants.A.Owner.UserId,
            (await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).OwnerId);
        Assert.Equal(activity + 1, await OwnershipTransferApi.ActivityCountAsync(factory.Services, tenants.A.OrganizationId));

        using (var again = await OwnershipTransferApi.DeclineAsync(adminHttp, transferId))
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        using (var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword))
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal(OwnershipTransferStatus.Declined, (await OwnershipTransferApi.TransferAsync(factory.Services, transferId)).Status);
        Assert.Equal(activity + 1, await OwnershipTransferApi.ActivityCountAsync(factory.Services, tenants.A.OrganizationId));
    }

    [Theory]
    [InlineData(OrgRole.Owner)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA127_Rechazar_DelOwnerODeOtroMiembro_Responde404YLaPropuestaSiguePendiente(string role)
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var ownerHttp = tenants.ClientFor(tenants.A);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        using var http = role == OrgRole.Owner ? tenants.ClientFor(tenants.A) : tenants.ClientFor(tenants.A, tenants.A.Member(role));

        using var response = await OwnershipTransferApi.DeclineAsync(http, transferId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(factory.Services, transferId)).Status);
    }

    [Fact]
    public async Task PLAN0018_CA127_TrasCancelarElOwner_AcceptYDeclineDelDestinoResponden404()
    {
        var tenants = await SeedAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var ownerHttp = tenants.ClientFor(tenants.A);
        using var adminHttp = tenants.ClientFor(tenants.A, admin);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        using (var cancel = await OwnershipTransferApi.CancelAsync(ownerHttp))
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        using (var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, transferId, MemberPassword))
            Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        using (var decline = await OwnershipTransferApi.DeclineAsync(adminHttp, transferId))
            Assert.Equal(HttpStatusCode.NotFound, decline.StatusCode);

        Assert.Equal(OwnershipTransferStatus.Cancelled, (await OwnershipTransferApi.TransferAsync(factory.Services, transferId)).Status);
        Assert.Equal(tenants.A.Owner.UserId,
            (await OrganizationDeletionApi.OrganizationAsync(factory.Services, tenants.A.OrganizationId)).OwnerId);
    }

    // ── CA-60: ActivityLog de cada paso de la transferencia ─────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA60_ProponerCancelarRechazarYAceptar_DejanCadaUnoSuFilaConActorObjetivoYValores()
    {
        var tenants = await SeedAsync();
        var owner = tenants.A.Owner;
        var admin = tenants.A.Member(OrgRole.Admin);
        using var ownerHttp = tenants.ClientFor(tenants.A);
        using var adminHttp = tenants.ClientFor(tenants.A, admin);

        var cancelled = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        using (var cancel = await OwnershipTransferApi.CancelAsync(ownerHttp))
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        var declined = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        using (var decline = await OwnershipTransferApi.DeclineAsync(adminHttp, declined))
            Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);
        var accepted = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);
        using (var accept = await OwnershipTransferApi.AcceptAsync(adminHttp, accepted, MemberPassword))
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);

        var logs = (await OwnershipTransferApi.ActivityAsync(factory.Services, tenants.A.OrganizationId))
            .Where(a => a.EntityType == ProposeOwnershipTransferCommandHandler.EntityType)
            .ToList();

        Assert.Equal(3, logs.Count(a => a.Action == ProposeOwnershipTransferCommandHandler.LogAction));

        var cancelLog = Assert.Single(logs, a => a.Action == CancelOwnershipTransferCommandHandler.LogAction);
        Assert.Equal((cancelled, owner.UserId), (cancelLog.EntityId, cancelLog.UserId));
        AssertMetadata(cancelLog, ("fromUserId", owner.UserId.ToString()), ("toUserId", admin.UserId.ToString()));

        var declineLog = Assert.Single(logs, a => a.Action == DeclineOwnershipTransferCommandHandler.LogAction);
        Assert.Equal((declined, admin.UserId), (declineLog.EntityId, declineLog.UserId));
        AssertMetadata(declineLog,
            ("actorUserId", admin.UserId.ToString()), ("fromUserId", owner.UserId.ToString()),
            ("before", OwnershipTransferStatus.Pending), ("after", OwnershipTransferStatus.Declined));

        var acceptLog = Assert.Single(logs, a => a.Action == AcceptOwnershipTransferCommandHandler.LogAction);
        Assert.Equal((accepted, admin.UserId), (acceptLog.EntityId, acceptLog.UserId));
        AssertMetadata(acceptLog,
            ("actorUserId", admin.UserId.ToString()), ("ownerBefore", owner.UserId.ToString()),
            ("ownerAfter", admin.UserId.ToString()), ("toRoleBefore", OrgRole.Admin), ("toRoleAfter", OrgRole.Owner),
            ("fromRoleAfter", OrgRole.Admin));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<TenantPair> SeedAsync()
        => OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword);

    private static void AssertMetadata(ActivityLog log, params (string Name, string Value)[] expected)
    {
        Assert.NotNull(log.Metadata);
        using var metadata = JsonDocument.Parse(log.Metadata!);
        foreach (var (name, value) in expected)
            Assert.Equal(value, metadata.RootElement.GetProperty(name).GetString(), ignoreCase: true);
    }
}

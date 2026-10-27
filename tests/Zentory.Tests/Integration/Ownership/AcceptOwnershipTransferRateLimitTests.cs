using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zentory.API.Authorization;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-111 (punto rev. 4: RF-81 f, g; RF-83 b) con reauth:{sub} en su valor por defecto (5 cada 15 min), el
// reloj simulado y los logs capturados: los intentos de aceptar del destino suman en el mismo contador; el 6.º, con la
// contraseña correcta, responde 429 sin efecto; pasados 15 min, el mismo intento responde 204. El 404 uniforme va antes
// del rate limit (RF-83 b 1): quien no es el destino no cuenta intentos ni recibe 429.
public sealed class AcceptOwnershipTransferRateLimitTests : IClassFixture<ReauthRateLimitApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Limite-Aceptar-2026!";
    private const string MemberPassword = "Miembro-Limite-Aceptar-2026!";
    private const string WrongPassword  = "Incorrecta-Limite-Aceptar-Trampa-2026!";
    private const int    Attempts       = 5;

    private readonly ReauthRateLimitApiFactory      _factory;
    private readonly FakeTimeProvider               _time;
    private readonly CapturingLoggerProvider        _logs = new();
    private readonly WebApplicationFactory<Program> _host;

    public AcceptOwnershipTransferRateLimitTests(ReauthRateLimitApiFactory factory)
    {
        _factory = factory;
        _time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        _host = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.AddProvider(_logs))
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_time);
            }));
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task PLAN0018_CA111_DestinoConCincoFallosAlAceptar_ElSextoCorrectoResponde429YPasados15MinResponde204()
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword);
        var target = tenants.A.Member(OrgRole.Admin);
        using var ownerHttp  = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        using var targetHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, target);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var rejected = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId, WrongPassword);
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(rejected);
        }

        using (var limited = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId, MemberPassword))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Contains(AuthRateLimitAttribute.RateLimitedCode, await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.True(int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After"))) > 0);
        }
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
        Assert.Equal(tenants.A.Owner.UserId,
            (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);

        // Cada fallo dejó AuditLog con motivo genérico; ni la contraseña ni el cuerpo aparecen en logs ni en AuditLog.
        Assert.Equal(Attempts, await FailureAuditsAsync(target.UserId));
        foreach (var secret in new[] { WrongPassword, MemberPassword, "currentPassword" })
            Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains(secret, StringComparison.Ordinal));

        _time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        using var allowed = await OwnershipTransferApi.AcceptAsync(targetHttp, transferId, MemberPassword);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(target.UserId, (await OrganizationDeletionApi.OrganizationAsync(_host.Services, tenants.A.OrganizationId)).OwnerId);
    }

    [Fact]
    public async Task PLAN0018_CA111_QuienNoEsElDestinoConElLimiteAgotado_Recibe404Y429No_SinContarIntentos()
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword);
        var target = tenants.A.Member(OrgRole.Admin);
        var member = tenants.A.Member(OrgRole.Member);
        using var ownerHttp  = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        using var memberHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, member);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);

        // El member agota su reauth:{sub} con sus propios rechazos al aceptar una propuesta que sí es suya (de B).
        var ownTransfer = await OwnTransferAsync(tenants, member);
        var ownHttp = memberHttp;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            using var rejected = await OwnershipTransferApi.AcceptAsync(ownHttp, ownTransfer, WrongPassword);
            await OwnershipTransferApi.AssertReauthenticationFailedAsync(rejected);
        }
        using (var limited = await OwnershipTransferApi.AcceptAsync(ownHttp, ownTransfer, MemberPassword))
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // Sobre la propuesta de otro, el 404 uniforme llega antes del rate limit.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var foreign = await OwnershipTransferApi.AcceptAsync(memberHttp, transferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        Assert.Equal(OwnershipTransferStatus.Pending, (await OwnershipTransferApi.TransferAsync(_host.Services, transferId)).Status);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Una propuesta pendiente de B hacia el usuario (que pasa a ser admin verificado de B).
    private async Task<Guid> OwnTransferAsync(TenantPair tenants, User user)
    {
        await OwnershipTransferApi.AddMemberAsync(_host.Services, tenants.B.OrganizationId, user, OrgRole.Admin, tenants.B.Owner.UserId);
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var transfer = OrganizationOwnershipTransfer.Propose(tenants.B.OrganizationId, tenants.B.Owner.UserId, user.UserId,
            new string('a', OrganizationOwnershipTransfer.PreviewHashLength), _time.GetUtcNow().UtcDateTime,
            TimeSpan.FromDays(7));
        db.OrganizationOwnershipTransfers.Add(transfer);
        await db.SaveChangesAsync();
        return transfer.Id;
    }

    private async Task<int> FailureAuditsAsync(Guid userId)
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.UserId == userId && a.Action == ReauthenticationVerifier.AuditAction)
            .ToListAsync();
        Assert.All(rows, row =>
        {
            Assert.Contains(ReauthenticationVerifier.AuditReason, row.NewValues, StringComparison.Ordinal);
            Assert.DoesNotContain(WrongPassword, row.NewValues, StringComparison.Ordinal);
        });
        return rows.Count;
    }
}

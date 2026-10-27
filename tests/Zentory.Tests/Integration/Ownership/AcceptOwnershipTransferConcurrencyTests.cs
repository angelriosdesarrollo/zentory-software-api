using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-128 (RF-83 e; RF-65 b2; SR-0018.37) y la carrera de RF-83 c con el cambio de rol o la baja del destino
// (deuda de TK-0069), con los tokens de concurrencia: ConcurrencyStamp de Organization, Status de la propuesta y
// OwnershipStamp del destino (D-45). Cada carrera corre de forma determinista en los dos órdenes:
//   - la otra operación confirma después de la credencial de la primera (ReauthVerifierSpy.AfterVerify), así la primera
//     lee el estado ya cambiado; o
//   - la otra operación confirma justo antes de que la primera escriba (HookedUnitOfWork), así la primera escribe con los
//     sellos ya viejos y el token la detiene.
// Tras cada caso, un solo ganador y la invariante de RF-48 e (MembershipInvariant).
public sealed class AcceptOwnershipTransferConcurrencyTests : IClassFixture<ZentoryApiFactory>, IDisposable
{
    private const string OwnerPassword  = "Owner-Carrera-Aceptar-2026!";
    private const string MemberPassword = "Miembro-Carrera-Aceptar-2026!";

    private readonly ZentoryApiFactory              _factory;
    private readonly ReauthVerifierSpy              _spy = new();
    private readonly WebApplicationFactory<Program> _host;

    // Se ejecuta una vez justo antes del próximo SaveChanges de cualquier comando del host.
    private Func<Task>? _beforeSave;

    public AcceptOwnershipTransferConcurrencyTests(ZentoryApiFactory factory)
    {
        _factory = factory;
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            _spy.Install(services);
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(sp =>
                new HookedUnitOfWork(new UnitOfWork(sp.GetRequiredService<ZentoryDbContext>()), TakeHook));
        }));
    }

    public void Dispose() => _host.Dispose();

    // ── Aceptar y eliminar la organización ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA128_AceptarYEliminar_GanaElBorrado_LaAceptacionNoTieneEfecto()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var delete = await OrganizationDeletionApi.DeleteAsync(s.OwnerHttp, s.Tenants.A.Organization.Name, OwnerPassword);
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        });

        using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        Assert.True(accept.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict, accept.StatusCode.ToString());
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, s.Tenants.A.OrganizationId);
        Assert.NotNull(organization.DeletedAt);
        Assert.Equal(s.Tenants.A.Owner.UserId, organization.OwnerId);
        Assert.Equal(OwnershipTransferStatus.Cancelled, await StatusAsync(s));
        await MembershipInvariant.AssertHoldsAsync(_host.Services, s.Tenants);
    }

    [Fact]
    public async Task PLAN0018_CA128_AceptarYEliminar_GanaLaAceptacion_ElBorradoResponde409SinEfecto()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        });

        using var delete = await OrganizationDeletionApi.DeleteAsync(s.OwnerHttp, s.Tenants.A.Organization.Name, OwnerPassword);

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, s.Tenants.A.OrganizationId);
        Assert.Null(organization.DeletedAt);
        Assert.Equal(s.Target.UserId, organization.OwnerId);
        Assert.Equal(OwnershipTransferStatus.Accepted, await StatusAsync(s));
        await MembershipInvariant.AssertHoldsAsync(_host.Services, s.Tenants);
    }

    // ── Aceptar y rechazar ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA128_AceptarYRechazar_GanaElRechazo_LaAceptacionResponde404()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var decline = await OwnershipTransferApi.DeclineAsync(s.TargetHttp, s.TransferId);
            Assert.Equal(HttpStatusCode.NoContent, decline.StatusCode);
        });

        using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        await AssertOwnerAsync(s, s.Tenants.A.Owner.UserId, OwnershipTransferStatus.Declined);
    }

    [Fact]
    public async Task PLAN0018_CA128_AceptarYRechazar_GanaLaAceptacion_ElRechazoPierdeElTokenYResponde404()
    {
        var s = await ProposedAsync();
        _beforeSave = async () =>
        {
            using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        };

        using var decline = await OwnershipTransferApi.DeclineAsync(s.TargetHttp, s.TransferId);

        Assert.Null(_beforeSave);
        Assert.Equal(HttpStatusCode.NotFound, decline.StatusCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
    }

    // ── Aceptar y cancelar (owner) ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA128_AceptarYCancelar_GanaLaCancelacion_LaAceptacionResponde404()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var cancel = await OwnershipTransferApi.CancelAsync(s.OwnerHttp);
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        });

        using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        await AssertOwnerAsync(s, s.Tenants.A.Owner.UserId, OwnershipTransferStatus.Cancelled);
    }

    [Fact]
    public async Task PLAN0018_CA128_AceptarYCancelar_GanaLaAceptacion_LaCancelacionPierdeElTokenSinEfecto()
    {
        var s = await ProposedAsync();
        _beforeSave = async () =>
        {
            using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        };

        using var cancel = await OwnershipTransferApi.CancelAsync(s.OwnerHttp);

        Assert.Null(_beforeSave);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
    }

    // ── Dos aceptaciones ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA128_DosAceptaciones_LaSegundaLeeLaResueltaYResponde404_UnSoloCambioDeOwner()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var first = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        });

        using var second = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
        await AssertSingleAcceptLogAsync(s);
    }

    [Fact]
    public async Task PLAN0018_CA128_DosAceptaciones_LaSegundaEscribeConLosSellosViejos_ElTokenLaDetieneYResponde404()
    {
        var s = await ProposedAsync();
        _beforeSave = async () =>
        {
            using var first = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        };

        using var second = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        Assert.Null(_beforeSave);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
        await AssertSingleAcceptLogAsync(s);
    }

    // ── Cambio de rol o baja del destino frente a la aceptación (deuda de TK-0069) ──────────────────────────────

    [Fact]
    public async Task PLAN0018_CA128_CambioDeRolDelDestinoYAceptacion_GanaLaAceptacion_ElCambioResponde409YNoDegradaAlNuevoOwner()
    {
        var s = await ProposedAsync();
        _beforeSave = async () =>
        {
            using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        };

        using var change = await MemberManagementApi.ChangeRoleAsync(s.OwnerHttp, s.Target.UserId, OrgRole.Member);

        Assert.Null(_beforeSave);
        await OwnershipTransferApi.AssertConflictAsync(change, ChangeMemberRoleCommandHandler.ChangedCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
        Assert.Equal(OrgRole.Owner, (await OwnershipTransferApi.RolesAsync(_host.Services, s.Tenants.A.OrganizationId))[s.Target.UserId]);
    }

    [Fact]
    public async Task PLAN0018_CA128_CambioDeRolDelDestinoYAceptacion_GanaElCambio_LaAceptacionLeeElRolNuevoYDejaUnSoloOwner()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var change = await MemberManagementApi.ChangeRoleAsync(s.OwnerHttp, s.Target.UserId, OrgRole.Member);
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        });

        using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        // El cambio de rol no invalida la propuesta: el destino sigue siendo miembro vigente y la acepta después.
        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
        var roles = await OwnershipTransferApi.RolesAsync(_host.Services, s.Tenants.A.OrganizationId);
        Assert.Equal((OrgRole.Owner, OrgRole.Admin), (roles[s.Target.UserId], roles[s.Tenants.A.Owner.UserId]));
    }

    [Fact]
    public async Task PLAN0018_CA128_BajaDelDestinoYAceptacion_GanaLaAceptacion_LaBajaResponde409YElNuevoOwnerSigueVigente()
    {
        var s = await ProposedAsync();
        _beforeSave = async () =>
        {
            using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);
            Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        };

        using var removal = await MemberManagementApi.RemoveAsync(s.OwnerHttp, s.Target.UserId);

        Assert.Null(_beforeSave);
        await OwnershipTransferApi.AssertConflictAsync(removal, RemoveMemberCommandHandler.ChangedCode);
        await AssertOwnerAsync(s, s.Target.UserId, OwnershipTransferStatus.Accepted);
        Assert.Equal(OrgRole.Owner, (await OwnershipTransferApi.RolesAsync(_host.Services, s.Tenants.A.OrganizationId))[s.Target.UserId]);
    }

    [Fact]
    public async Task PLAN0018_CA128_BajaDelDestinoYAceptacion_GanaLaBaja_LaAceptacionResponde404()
    {
        var s = await ProposedAsync();
        OnceAfterVerify(async () =>
        {
            using var removal = await MemberManagementApi.RemoveAsync(s.OwnerHttp, s.Target.UserId);
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        });

        using var accept = await OwnershipTransferApi.AcceptAsync(s.TargetHttp, s.TransferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        await AssertOwnerAsync(s, s.Tenants.A.Owner.UserId, OwnershipTransferStatus.Cancelled);
        Assert.DoesNotContain(s.Target.UserId, (await OwnershipTransferApi.RolesAsync(_host.Services, s.Tenants.A.OrganizationId)).Keys);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Scenario(TenantPair Tenants, User Target, HttpClient OwnerHttp, HttpClient TargetHttp, Guid TransferId);

    // Owner O (pro, para que ManageMembers deje cambiar roles y dar de baja) con una propuesta pendiente hacia el admin A.
    private async Task<Scenario> ProposedAsync()
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(_factory, _host.Services, OwnerPassword, MemberPassword,
            configureA: a => a.WithPlan(Plan.Pro));
        var target = tenants.A.Member(OrgRole.Admin);
        var ownerHttp  = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A);
        var targetHttp = OrganizationDeletionApi.ClientFor(_host, tenants, tenants.A, target);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, target.UserId, OwnerPassword);
        return new Scenario(tenants, target, ownerHttp, targetHttp, transferId);
    }

    private void OnceAfterVerify(Func<Task> competitor)
        => _spy.AfterVerify = async () =>
        {
            _spy.AfterVerify = null;
            await competitor();
        };

    private Func<Task>? TakeHook() => Interlocked.Exchange(ref _beforeSave, null);

    private async Task<string> StatusAsync(Scenario s)
        => (await OwnershipTransferApi.TransferAsync(_host.Services, s.TransferId)).Status;

    // Un solo ganador: el owner es el esperado, la propuesta quedó en el estado del ganador y la invariante se cumple.
    private async Task AssertOwnerAsync(Scenario s, Guid expectedOwner, string expectedStatus)
    {
        var organization = await OrganizationDeletionApi.OrganizationAsync(_host.Services, s.Tenants.A.OrganizationId);
        Assert.Equal(expectedOwner, organization.OwnerId);
        Assert.Null(organization.DeletedAt);
        Assert.Equal(expectedStatus, await StatusAsync(s));
        await MembershipInvariant.AssertHoldsAsync(_host.Services, s.Tenants);
    }

    private async Task AssertSingleAcceptLogAsync(Scenario s)
        => Assert.Single(await OwnershipTransferApi.ActivityAsync(_host.Services, s.Tenants.A.OrganizationId),
            a => a.Action == AcceptOwnershipTransferCommandHandler.LogAction);
}

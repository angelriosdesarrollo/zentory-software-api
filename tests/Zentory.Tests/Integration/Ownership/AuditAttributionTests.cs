using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.InvitationAcceptApi;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 REV-0005 H-2 (RF-83, RF-50; D-21): el AuditInterceptor atribuye el AuditLog de una membresía a la organización
// de la fila (OrganizationId de OrganizationMember), no a la activa del token, y decide "solo empresa" con el LegalType de
// ESA organización. La aceptación de una transferencia y la de una invitación escriben membresías de una organización
// distinta de la activa de quien llama; su rastro va a la organización dueña del dato, sea cual sea el tipo de la activa.
public sealed class AuditAttributionTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string OwnerPassword  = "Owner-Auditoria-2026!";
    private const string MemberPassword = "Miembro-Auditoria-2026!";

    private const string MembersTable = "organization_members";

    [Theory]
    [InlineData(LegalType.Freelance)]
    [InlineData(LegalType.Empresa)]
    public async Task PLAN0018_REV5_H2_AceptarTransferencia_ConOtraOrganizacionActiva_AuditaLasMembresiasEnLaOrganizacionTransferida(string activeLegalType)
    {
        var tenants = await OwnershipTransferApi.OrganizationAsync(factory, factory.Services, OwnerPassword, MemberPassword,
            configureA: a => a.WithPlan(Plan.Studio), configureB: b => b.WithLegalType(activeLegalType));
        var admin = tenants.A.Member(OrgRole.Admin);
        using var ownerHttp = OrganizationDeletionApi.ClientFor(factory, tenants, tenants.A, tenants.A.Owner);
        var transferId = await OwnershipTransferApi.ProposeTransferIdAsync(ownerHttp, admin.UserId, OwnerPassword);

        // El destino es member de B y tiene B activa al aceptar.
        await OwnershipTransferApi.AddMemberAsync(factory.Services, tenants.B.OrganizationId, admin, OrgRole.Member, tenants.B.Owner.UserId);
        using var inB = AuthSessionApi.Bearer(factory, factory.Tokens.ForUser(admin, tenants.B.Organization, OrgRole.Member));
        var before = await MembershipAuditAsync(tenants.A.OrganizationId, admin.UserId, tenants.A.Owner.UserId);

        using var accept = await OwnershipTransferApi.AcceptAsync(inB, transferId, MemberPassword);

        Assert.Equal(HttpStatusCode.NoContent, accept.StatusCode);
        var rows = (await MembershipAuditAsync(tenants.A.OrganizationId, admin.UserId, tenants.A.Owner.UserId))
            .Where(r => before.All(b => b.Id != r.Id)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("UPDATE", r.Action);
            Assert.Equal(tenants.A.OrganizationId, r.OrganizationId);
            Assert.Equal(admin.UserId, r.UserId);
        });
        Assert.DoesNotContain(await AuditRowsAsync(tenants.B.OrganizationId), r => r.TableName == MembersTable);
    }

    [Theory]
    [InlineData(LegalType.Freelance)]
    [InlineData(LegalType.Empresa)]
    public async Task PLAN0018_REV5_H2_AceptarInvitacion_ConJwtDeOtraOrganizacion_AuditaLaMembresiaEnLaOrganizacionInvitada(string activeLegalType)
    {
        var tenants = await new TenantBuilder(factory)
            .ForA(a => a.WithLegalType(LegalType.Empresa))
            .ForB(b => b.WithLegalType(activeLegalType))
            .BuildAsync();
        var invitee = tenants.B.Owner;
        await MarkEmailVerifiedAsync(factory.Services, invitee.UserId);
        var invitation = await SeedInvitationAsync(
            factory.Services, tenants.A.OrganizationId, invitee.Email, OrgRole.Member, tenants.A.Owner.UserId);
        using var http = factory.CreateClient();

        using var response = await AcceptWithBearerAsync(http, tenants.TokenFor(tenants.B), invitation.RawToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var membership = Assert.Single(await MembershipsOfAsync(factory.Services, invitee.UserId),
            m => m.OrganizationId == tenants.A.OrganizationId);
        var audit = Assert.Single(await AuditRowsAsync(tenants.A.OrganizationId),
            r => r.TableName == MembersTable && r.RecordId == membership.Id);
        Assert.Equal(("INSERT", invitee.UserId), (audit.Action, audit.UserId));
        Assert.DoesNotContain(await AuditRowsAsync(tenants.B.OrganizationId), r => r.TableName == MembersTable);
    }

    [Fact]
    public async Task PLAN0018_REV5_H2_CambiarRolEnLaOrganizacionActiva_SigueAuditandoseEnLaActiva()
    {
        var tenants = await MemberManagementApi.FullOrganizationAsync(factory);
        var actor = MemberManagementApi.Actor(tenants.A, OrgRole.Owner);
        var target = tenants.A.Member(OrgRole.Member);
        using var http = tenants.ClientFor(tenants.A, actor);

        using var response = await MemberManagementApi.ChangeRoleAsync(http, target.UserId, OrgRole.Admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var membership = (await MemberManagementApi.MembershipAsync(factory.Services, tenants.A.OrganizationId, target.UserId))!;
        var audit = Assert.Single(await AuditRowsAsync(tenants.A.OrganizationId),
            r => r.TableName == MembersTable && r.RecordId == membership.Id);
        Assert.Equal(("UPDATE", actor.UserId), (audit.Action, audit.UserId));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<List<AuditLog>> AuditRowsAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        return await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .ToListAsync();
    }

    // Filas de AuditLog de las membresías vigentes de dos usuarios en la organización, atribuidas a quien sea.
    private async Task<List<AuditLog>> MembershipAuditAsync(Guid organizationId, params Guid[] userIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var membershipIds = await db.OrganizationMembers.IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && userIds.Contains(m.UserId))
            .Select(m => m.Id)
            .ToListAsync();
        return await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TableName == MembersTable && membershipIds.Contains(a.RecordId))
            .ToListAsync();
    }
}

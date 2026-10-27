using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Tests.Support;
using static Zentory.Tests.Support.MemberManagementApi;

namespace Zentory.Tests.Integration.Members;

// PLAN-0018 T30 (CA-T30-1, parte de CA-54; RF-48 e, SR-0018.8): el chequeo MembershipInvariant que corre tras los
// escenarios de CA-54, CA-56 y CA-59 detecta de verdad un estado roto. InMemory no aplica el índice único de M4b, así que
// estos casos siembran a propósito los estados que en PostgreSQL rechazaría la base; cada uno en su propia organización.
public sealed class MembershipInvariantTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    [Fact]
    public async Task PLAN0018_CA54_InvarianteDeMembresias_OrganizacionConOwnerAdminsYMember_SeCumple()
    {
        var tenants = await FullOrganizationAsync(factory);

        Assert.Empty(await MembershipInvariant.ViolationsAsync(factory.Services, tenants.A.OrganizationId, tenants.B.OrganizationId));
        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_InvarianteDeMembresias_ExmiembroConMembresiaBorradaYOtraVigente_SeCumple()
    {
        // D-20: aceptar una invitación tras una baja crea una fila nueva; la borrada no cuenta (el índice es filtrado).
        var tenants = await FullOrganizationAsync(factory);
        var former = tenants.A.Member(OrgRole.Member);
        await AuthSessionApi.RemoveMembershipAsync(factory.Services, tenants.A.OrganizationId, former.UserId);
        await tenants.SeedAsync(OrganizationMember.Create(tenants.A.OrganizationId, former.UserId, OrgRole.Admin));

        await MembershipInvariant.AssertHoldsAsync(factory.Services, tenants);
    }

    [Fact]
    public async Task PLAN0018_CA54_InvarianteDeMembresias_DosVigentesDelMismoUsuario_LoDetecta()
    {
        var tenants = await FullOrganizationAsync(factory);
        var member = tenants.A.Member(OrgRole.Member);
        await tenants.SeedAsync(OrganizationMember.Create(tenants.A.OrganizationId, member.UserId, OrgRole.Member));

        var violation = Assert.Single(await MembershipInvariant.ViolationsAsync(factory.Services, tenants.A.OrganizationId));
        Assert.Contains(member.UserId.ToString(), violation, StringComparison.Ordinal);
        Assert.Empty(await MembershipInvariant.ViolationsAsync(factory.Services, tenants.B.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA54_InvarianteDeMembresias_MembresiaDelOwnerSinRolOwner_LoDetecta()
    {
        var tenants = await FullOrganizationAsync(factory);
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId, OrgRole.Admin);

        Assert.Single(await MembershipInvariant.ViolationsAsync(factory.Services, tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA54_InvarianteDeMembresias_SegundaMembresiaOwnerDeOtroUsuario_LoDetecta()
    {
        var tenants = await FullOrganizationAsync(factory);
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, Admins(tenants.A)[0].UserId, OrgRole.Owner);

        Assert.Single(await MembershipInvariant.ViolationsAsync(factory.Services, tenants.A.OrganizationId));
    }

    [Fact]
    public async Task PLAN0018_CA54_InvarianteDeMembresias_UnicaOwnerNoEsLaDeOwnerId_LoDetecta()
    {
        var tenants = await FullOrganizationAsync(factory);
        var admin = Admins(tenants.A)[0];
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, tenants.A.Owner.UserId, OrgRole.Admin);
        await SetStoredRoleAsync(factory.Services, tenants.A.OrganizationId, admin.UserId, OrgRole.Owner);

        Assert.Single(await MembershipInvariant.ViolationsAsync(factory.Services, tenants.A.OrganizationId));
    }
}

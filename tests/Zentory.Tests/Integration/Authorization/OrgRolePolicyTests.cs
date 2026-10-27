using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Authorization;

// PLAN-0018 RF-15 (CA-74 integración; CA-20 en parte, se cierra en T30c; SR-0018.7, SR-0018.8): las policies de rol
// sobre el host real, con la membresía que pone MembershipValidationMiddleware. Los tokens son reales y su claim
// active_org_role es el rol sembrado: los casos negativos prueban que el handler lee la base, no el claim.
public sealed class OrgRolePolicyTests(AuthorizationProbeApiFactory factory) : IClassFixture<AuthorizationProbeApiFactory>
{
    // ── CA-74: RequireFinancialAccess en una acción de prueba ───────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA74_AccionConRequireFinancialAccess_OwnerYAdminPasan_MemberRecibe403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory)
            .WithMember(OrgRole.Admin)
            .WithMember(OrgRole.Member)
            .BuildAsync();

        foreach (var user in new[] { tenants.A.Owner, tenants.A.Member(OrgRole.Admin) })
        {
            using var http = tenants.ClientFor(tenants.A, user);
            using var response = await http.GetAsync(AuthorizationProbeApiFactory.FinancialAccessPath);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<AuthorizationProbeApiFactory.ProbeResponse>();
            Assert.Equal(tenants.A.OrganizationId, body!.OrganizationId);
        }

        using var member = tenants.ClientFor(tenants.A, tenants.A.Member(OrgRole.Member));
        using var denied = await member.GetAsync(AuthorizationProbeApiFactory.FinancialAccessPath);
        await ForbiddenAssert.IsAccessDeniedAsync(denied);
    }

    [Fact]
    public async Task PLAN0018_CA74_AccionConRequireFinancialAccess_RolDegradadoEnLaBase_TokenConAdmin_Devuelve403()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        var admin = tenants.A.Member(OrgRole.Admin);
        using var http = tenants.ClientFor(tenants.A, admin);

        await SetRoleAsync(tenants.A.OrganizationId, admin.UserId, OrgRole.Member);

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.FinancialAccessPath);
        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    // ── CA-74: RequireOrgOwner exige rol owner y Organization.OwnerId == sub ────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA74_RequireOrgOwner_OwnerPasa_AdminYMemberReciben403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory)
            .WithMember(OrgRole.Admin)
            .WithMember(OrgRole.Member)
            .BuildAsync();

        using (var owner = tenants.ClientFor(tenants.A))
        using (var ok = await owner.GetAsync(AuthorizationProbeApiFactory.RequireOrgOwnerPath))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        foreach (var role in new[] { OrgRole.Admin, OrgRole.Member })
        {
            using var http = tenants.ClientFor(tenants.A, tenants.A.Member(role));
            using var response = await http.GetAsync(AuthorizationProbeApiFactory.RequireOrgOwnerPath);
            await ForbiddenAssert.IsAccessDeniedAsync(response);
        }
    }

    [Fact]
    public async Task PLAN0018_CA74_RequireOrgOwner_RolOwnerPeroOwnerIdDeOtroUsuario_Devuelve403AccessDenied()
    {
        var tenants = await new TenantBuilder(factory).WithMember(OrgRole.Admin).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        // El rol guardado sigue siendo owner, pero la organización ya pertenece a otro usuario (SR-0018.8).
        await SetOwnerAsync(tenants.A.OrganizationId, tenants.A.Member(OrgRole.Admin).UserId);

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.RequireOrgOwnerPath);
        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    // ── CA-20 (parte): acción excluida del middleware con RequireOrgOwner ───────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA20_AccionExcluidaConSkipMembershipValidation_RequireOrgOwner_ClaimOwner_Devuelve403()
    {
        var tenants = await new TenantBuilder(factory).BuildAsync();
        using var http = tenants.ClientFor(tenants.A);

        // Control: el mismo owner pasa la acción no excluida con la misma policy.
        using (var control = await http.GetAsync(AuthorizationProbeApiFactory.RequireOrgOwnerPath))
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);

        using var response = await http.GetAsync(AuthorizationProbeApiFactory.SkippedOrgOwnerPath);
        await ForbiddenAssert.IsAccessDeniedAsync(response);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────────────────────

    private async Task SetRoleAsync(Guid organizationId, Guid userId, string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var member = await db.OrganizationMembers.IgnoreQueryFilters()
            .SingleAsync(m => m.OrganizationId == organizationId && m.UserId == userId);
        member.ChangeRole(role);
        await db.SaveChangesAsync();
    }

    private async Task SetOwnerAsync(Guid organizationId, Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var organization = await db.Organizations.IgnoreQueryFilters()
            .SingleAsync(o => o.OrganizationId == organizationId);
        organization.SetOwner(ownerId);
        await db.SaveChangesAsync();
    }
}

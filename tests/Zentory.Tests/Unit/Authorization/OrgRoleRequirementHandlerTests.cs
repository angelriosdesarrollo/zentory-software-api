using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Extensions;
using Zentory.API.Middleware;
using Zentory.Domain.Constants;

namespace Zentory.Tests.Unit.Authorization;

// PLAN-0018 RF-15 (CA-74; SR-0018.7, SR-0018.8): el handler de OrgRoleRequirement, evaluado a través de las policies
// reales de AddAppAuthorization y del registro real de IMembershipContext (AddRequestContext). El claim active_org_role
// del principal dice siempre owner: si el handler lo leyera, los casos negativos pasarían.
public sealed class OrgRoleRequirementHandlerTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    public static TheoryData<string> AllRolePolicies => new()
    {
        PolicyNames.RequireOrgOwner,
        PolicyNames.RequireOrgAdmin,
        PolicyNames.RequireFinancialAccess,
    };

    public static TheoryData<string, string, bool> AdminAndFinancialByRole => new()
    {
        { PolicyNames.RequireOrgAdmin,        OrgRole.Owner,  true  },
        { PolicyNames.RequireOrgAdmin,        OrgRole.Admin,  true  },
        { PolicyNames.RequireOrgAdmin,        OrgRole.Member, false },
        { PolicyNames.RequireFinancialAccess, OrgRole.Owner,  true  },
        { PolicyNames.RequireFinancialAccess, OrgRole.Admin,  true  },
        { PolicyNames.RequireFinancialAccess, OrgRole.Member, false },
    };

    // ── Sin membresía resuelta ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllRolePolicies))]
    public async Task PLAN0018_CA74_MembershipContextSinPoblar_ClaimDiceOwner_Falla(string policy)
    {
        var sub = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(policy, sub, membership: null);

        Assert.False(succeeded);
    }

    // ── RequireOrgOwner ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA74_RequireOrgOwner_RolOwnerPeroOwnerIdDistintoDeSub_Falla()
    {
        var sub = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(PolicyNames.RequireOrgOwner, sub,
            new Membership(sub, OrgRole.Owner, OwnerUserId: Guid.NewGuid()));

        Assert.False(succeeded);
    }

    [Fact]
    public async Task PLAN0018_CA74_RequireOrgOwner_RolOwnerSinOwnerIdEnLaOrganizacion_Falla()
    {
        var sub = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(PolicyNames.RequireOrgOwner, sub,
            new Membership(sub, OrgRole.Owner, OwnerUserId: null));

        Assert.False(succeeded);
    }

    [Fact]
    public async Task PLAN0018_CA74_RequireOrgOwner_RolOwnerYOwnerIdIgualASub_Pasa()
    {
        var sub = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(PolicyNames.RequireOrgOwner, sub,
            new Membership(sub, OrgRole.Owner, OwnerUserId: sub));

        Assert.True(succeeded);
    }

    [Theory]
    [InlineData(OrgRole.Admin)]
    [InlineData(OrgRole.Member)]
    public async Task PLAN0018_CA74_RequireOrgOwner_RolNoOwnerAunqueOwnerIdIgualASub_Falla(string role)
    {
        var sub = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(PolicyNames.RequireOrgOwner, sub,
            new Membership(sub, role, OwnerUserId: sub));

        Assert.False(succeeded);
    }

    // ── RequireOrgAdmin y RequireFinancialAccess ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AdminAndFinancialByRole))]
    public async Task PLAN0018_CA74_RequireOrgAdminYFinancialAccess_PasanParaOwnerYAdmin_FallanParaMember(
        string policy, string role, bool expected)
    {
        var sub = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(policy, sub, new Membership(sub, role, OwnerUserId: Guid.NewGuid()));

        Assert.Equal(expected, succeeded);
    }

    [Theory]
    [MemberData(nameof(AllRolePolicies))]
    public async Task PLAN0018_CA74_MembresiaDeOtroUsuario_SubDistinto_Falla(string policy)
    {
        // La membresía resuelta es de otro usuario (owner de la organización): el principal no la hereda.
        var other = Guid.NewGuid();

        var succeeded = await AuthorizeAsync(policy, sub: Guid.NewGuid(),
            new Membership(other, OrgRole.Owner, OwnerUserId: other));

        Assert.False(succeeded);
    }

    // ── Infraestructura ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Membership(Guid UserId, string Role, Guid? OwnerUserId);

    private static async Task<bool> AuthorizeAsync(string policy, Guid sub, Membership? membership)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppAuthorization();
        services.AddRequestContext();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        if (membership is not null)
        {
            scope.ServiceProvider.GetRequiredService<MembershipContext>()
                .Resolve(OrganizationId, membership.UserId, membership.Role, membership.OwnerUserId);
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, sub.ToString()),
            new Claim("active_org_id", OrganizationId.ToString()),
            new Claim("active_org_role", OrgRole.Owner),
        ], authenticationType: "Test"));

        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy);
        return result.Succeeded;
    }
}

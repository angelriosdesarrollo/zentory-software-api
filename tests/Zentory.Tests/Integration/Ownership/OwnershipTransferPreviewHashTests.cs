using Zentory.Application.Organization.Services;
using Zentory.Domain.Constants;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 CA-89 (PREVIEW_STALE al aceptar; D-33), prueba unitaria de OwnershipTransferPreview.ComputeHash, la única
// función del previewHash (deuda de TK-0069): la aceptación lo recalcula, así que debe ser canónico. No depende del orden
// de lostFeatures ni de exceededLimits, da 64 caracteres hex en minúscula y cambia con cualquier valor del aviso.
public sealed class OwnershipTransferPreviewHashTests
{
    private static readonly Guid OrganizationId = Guid.Parse("6f1c3c1e-0d7a-4c43-9a55-2b0d6a1f3e01");
    private static readonly Guid TargetUserId   = Guid.Parse("0b9e8d7c-6a5f-4e3d-8c2b-1a0f9e8d7c6b");

    private static readonly string[] LostFeatures = ["Portal de colaboradores", "Cuentas de cobro", "PILA del equipo"];

    private static readonly ExceededLimitDto[] ExceededLimits =
    [
        new(PlanLimits.FeatureKeys.MaxOrgMembers, 7, 5),
        new(PlanLimits.FeatureKeys.MaxClients, 30, 20),
        new(PlanLimits.FeatureKeys.MaxProjects, 12, 10),
    ];

    private static string Hash(
        IEnumerable<string>? lostFeatures = null, IEnumerable<ExceededLimitDto>? exceededLimits = null,
        string currentPlan = Plan.Studio, string newPlan = Plan.Pro, bool blocked = false,
        Guid? organizationId = null, Guid? targetUserId = null)
        => OwnershipTransferPreview.ComputeHash(
            organizationId ?? OrganizationId, targetUserId ?? TargetUserId, currentPlan, newPlan,
            lostFeatures ?? LostFeatures, exceededLimits ?? ExceededLimits, blocked);

    [Fact]
    public void PLAN0018_CA89_ComputeHash_OrdenDeLostFeaturesYExceededLimits_NoCambiaElHash()
    {
        var reference = Hash();

        Assert.Equal(reference, Hash(lostFeatures: LostFeatures.Reverse()));
        Assert.Equal(reference, Hash(exceededLimits: ExceededLimits.Reverse()));
        Assert.Equal(reference, Hash(LostFeatures.Order(StringComparer.Ordinal), ExceededLimits.OrderBy(l => l.Limit)));
    }

    [Fact]
    public void PLAN0018_CA89_ComputeHash_Son64CaracteresHexEnMinusculaYEsDeterminista()
    {
        var hash = Hash();

        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, Hash());
        Assert.Equal(hash, Hash([.. LostFeatures], [.. ExceededLimits]));
    }

    [Fact]
    public void PLAN0018_CA89_ComputeHash_CualquierValorDelAvisoDistinto_CambiaElHash()
    {
        var reference = Hash();

        string[] variants =
        [
            Hash(currentPlan: Plan.Pro),
            Hash(newPlan: Plan.Free),
            Hash(blocked: true),
            Hash(organizationId: Guid.NewGuid()),
            Hash(targetUserId: Guid.NewGuid()),
            Hash(lostFeatures: LostFeatures.Skip(1)),
            Hash(exceededLimits: [ExceededLimits[0] with { Usage = 8 }, ExceededLimits[1], ExceededLimits[2]]),
            Hash(exceededLimits: [ExceededLimits[0] with { Limit = 6 }, ExceededLimits[1], ExceededLimits[2]]),
            Hash(exceededLimits: ExceededLimits.Take(2)),
        ];

        Assert.All(variants, variant => Assert.NotEqual(reference, variant));
        Assert.Equal(variants.Length, variants.Distinct(StringComparer.Ordinal).Count());
    }
}

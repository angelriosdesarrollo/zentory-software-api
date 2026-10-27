using System.Reflection;
using System.Text.RegularExpressions;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 CA-3 (test de política): la matriz legalType × plan × orgRole de FEATURE_PERMISSIONS §1, §2 y §4 la cubren
// CA-21, CA-46, CA-47, CA-48, CA-52, CA-53, CA-86, CA-87 y CA-126. Por reflexión sobre este ensamblado, cada uno de los
// criterios del backend tiene al menos un test xUnit ([Fact] o [Theory]) llamado PLAN0018_CA<n>_. CA-48 es vitest
// (zentory-app, T26) y se cita en la evidencia, no aquí.
public sealed partial class MatrixCoverageTests
{
    // Criterios del backend que cubren la matriz de CA-3.
    public static readonly int[] MatrixCriteria = [21, 46, 47, 52, 53, 86, 87, 126];

    [Fact]
    public void PLAN0018_CA3_MatrixCoverage_CadaCriterioDeLaMatriz_TieneAlMenosUnTestXunit()
    {
        var covered = CoveredCriteria(typeof(MatrixCoverageTests).Assembly);

        var missing = MatrixCriteria.Where(n => !covered.ContainsKey(n)).Select(n => $"CA-{n}").ToList();

        Assert.True(missing.Count == 0,
            "Criterios de la matriz de CA-3 sin ningún test PLAN0018_CA<n>_: " + string.Join(", ", missing));
    }

    [Fact]
    public void PLAN0018_CA3_MatrixCoverage_ElRecorrido_CuentaSoloMetodosDeTestYElNumeroExacto()
    {
        // Control del anterior: el recorrido ve tests reales, no confunde CA-12 con CA-126 y no cuenta lo que no es test.
        var covered = CoveredCriteria(typeof(MatrixCoverageTests).Assembly);

        Assert.True(covered.Count > MatrixCriteria.Length, $"Solo se vieron {covered.Count} criterios.");
        Assert.Contains(nameof(PLAN0018_CA3_MatrixCoverage_CadaCriterioDeLaMatriz_TieneAlMenosUnTestXunit), covered[3]);
        Assert.Equal(126, CriterionOf("PLAN0018_CA126_Algo"));
        Assert.Equal(12, CriterionOf("PLAN0018_CA12_Algo"));
        Assert.Null(CriterionOf("PLAN0018_CA_Algo"));
        Assert.Null(CriterionOf("PLAN0017_CA21_Algo"));
        // Un método con el nombre del patrón pero sin [Fact] ni [Theory] no cuenta.
        Assert.Equal(9999, CriterionOf(nameof(PLAN0018_CA9999_SinAtributoDeTest)));
        Assert.DoesNotContain(9999, covered.Keys);
    }

    // Sin [Fact]: su nombre no debe contar como cobertura.
    private static void PLAN0018_CA9999_SinAtributoDeTest() { }

    private static Dictionary<int, List<string>> CoveredCriteria(Assembly assembly)
        => assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any())
            .Select(method => (Criterion: CriterionOf(method.Name), method.Name))
            .Where(m => m.Criterion is not null)
            .GroupBy(m => m.Criterion!.Value)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Name).ToList());

    private static int? CriterionOf(string methodName)
    {
        var match = TestNamePattern().Match(methodName);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"^PLAN0018_CA(\d+)_")]
    private static partial Regex TestNamePattern();
}

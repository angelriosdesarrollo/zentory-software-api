using System.Text.RegularExpressions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Schema;

// PLAN-0001 (CA-15): la suite corre completa. El único Skip admitido es el del CA-18 si Npgsql intentara conectarse
// (ADR-0001 §Decisión 5); cualquier otro test apagado tiene que fallar aquí en vez de pasar desapercibido.
// PLAN-0018 (RF-8, REV-0003 H-11): vive en su propio archivo desde que se borró el test temporal de PostgresModelTests.
public sealed class TestSuiteSkipPolicyTests
{
    private static readonly Regex SkippedTest = new(@"\[\s*(Fact|Theory)\s*\([^\]]*\bSkip\s*=", RegexOptions.Compiled);

    private const string AllowedFile = "tests/Zentory.Tests/Integration/Schema/PostgresModelTests.cs";

    [Fact]
    public void PLAN0001_CA15_Suite_SinTestsSkip_SalvoPostgresModel()
    {
        var disallowed = DisallowedSkips(TestSourceFiles());

        Assert.True(disallowed.Count == 0, "Tests con Skip fuera de " + AllowedFile + ": " + string.Join(", ", disallowed));
    }

    [Fact]
    public void PLAN0018_CA14_SkipPolicy_EnSuPropioArchivo_DetectaSkipNoPermitido()
    {
        // La política ya no comparte archivo con PostgresModelTests: borrar ese test no puede arrastrarla.
        Assert.True(File.Exists(Path.Combine(RepoRoot.Path, "tests", "Zentory.Tests", "Integration", "Schema",
            "TestSuiteSkipPolicyTests.cs")));
        Assert.DoesNotContain(nameof(TestSuiteSkipPolicyTests),
            File.ReadAllText(Path.Combine(RepoRoot.Path, AllowedFile)), StringComparison.Ordinal);

        // Control del detector: las formas de un Skip se arman por partes para que este archivo no se delate solo.
        var skippedFact    = "[" + "Fact(Skip = \"apagado\")]";
        var skippedTheory  = "[ " + "Theory( DisplayName = \"x\", Skip = \"apagado\" )]";
        var plainFact      = "[" + "Fact]";
        var displayOnly    = "[" + "Fact(DisplayName = \"SkipList\")]";

        Assert.Matches(SkippedTest, skippedFact);
        Assert.Matches(SkippedTest, skippedTheory);
        Assert.DoesNotMatch(SkippedTest, plainFact);
        Assert.DoesNotMatch(SkippedTest, displayOnly);

        // Y la regla señala el archivo no permitido, pero no el permitido ni uno sin Skip.
        const string otherFile = "tests/Zentory.Tests/Unit/Build/SomethingTests.cs";
        const string cleanFile = "tests/Zentory.Tests/Unit/Build/CleanTests.cs";
        var disallowed = DisallowedSkips([(otherFile, skippedFact), (AllowedFile, skippedTheory), (cleanFile, plainFact)]);

        Assert.Equal([otherFile], disallowed);
    }

    private static List<string> DisallowedSkips(IEnumerable<(string File, string Text)> files)
        => files
            .Where(source => source.File != AllowedFile && SkippedTest.IsMatch(source.Text))
            .Select(source => source.File)
            .ToList();

    private static IEnumerable<(string File, string Text)> TestSourceFiles()
    {
        var tests = Path.Combine(RepoRoot.Path, "tests");

        return Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"))
            .Select(file => (file, File.ReadAllText(Path.Combine(RepoRoot.Path, file))));
    }
}

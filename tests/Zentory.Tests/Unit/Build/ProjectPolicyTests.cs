using System.Text.Json;
using System.Xml.Linq;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0001 (RF-9, RF-10): política de dependencias del repo verificada sobre los archivos de proyecto,
// para que una versión flotante, una librería de tests no acordada o una bajada de EF Core no pasen en silencio.
public sealed class ProjectPolicyTests
{
    private const string ApiProject            = "src/Zentory.API/Zentory.API.csproj";
    private const string InfrastructureProject = "src/Zentory.Infrastructure/Zentory.Infrastructure.csproj";
    private const string TestsProject          = "tests/Zentory.Tests/Zentory.Tests.csproj";
    private const string ApiAssetsFile         = "src/Zentory.API/obj/project.assets.json";
    private const string ApiTargetFramework    = "net8.0";
    private const string EfCoreRuntimeVersion  = "8.0.32";


    public static TheoryData<string, string, string> PinnedPackages => new()
    {
        { ApiProject,            "Microsoft.EntityFrameworkCore.Design", "8.0.32" },
        { InfrastructureProject, "Hangfire.AspNetCore",                  "1.8.25" },
        { InfrastructureProject, "Hangfire.InMemory",                    "1.0.0" },
        { InfrastructureProject, "Hangfire.PostgreSql",                  "1.20.13" },
        { InfrastructureProject, "Microsoft.Extensions.Http",            "8.0.1" },
        { InfrastructureProject, "Resend",                               "0.4.0" },
        { InfrastructureProject, "AWSSDK.S3",                            "3.7.511.8" },
        { InfrastructureProject, "QuestPDF",                             "2024.10.4" },
        { InfrastructureProject, "Google.Apis.Auth",                     "1.68.0" },
        { TestsProject,          "Microsoft.AspNetCore.Mvc.Testing",     "8.0.11" },
    };

    [Fact]
    public void PLAN0001_CA13_ProjectFiles_UnderSrcAndTests_HaveNoFloatingVersions()
    {
        var floating = EnumerateProjectFiles()
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Select(element => (file, element.Name.LocalName, Version: VersionOf(element)))
                .Where(item => item.Version is not null && item.Version.Contains('*')))
            .Select(item => $"{Relative(item.file)}: <{item.LocalName}> Version=\"{item.Version}\"")
            .ToList();

        Assert.Empty(floating);
    }

    [Theory]
    [MemberData(nameof(PinnedPackages))]
    public void PLAN0001_CA13_PinnedPackage_InItsProject_HasExactVersion(string project, string package, string expected)
    {
        var references = PackageReferences(project)
            .Where(reference => string.Equals(reference.Name, package, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var reference = Assert.Single(references);
        Assert.Equal(expected, reference.Version);
    }

    [Fact]
    public void PLAN0001_CA14_TestsProject_ReferencesApiAndInfrastructure()
    {
        var projectReferences = Elements(TestsProject, "ProjectReference")
            .Select(element => Path.GetFileName(NormalizeSeparators((string?)element.Attribute("Include") ?? "")))
            .ToList();

        Assert.Contains("Zentory.API.csproj", projectReferences);
        Assert.Contains("Zentory.Infrastructure.csproj", projectReferences);
    }

    [Fact]
    public void PLAN0001_CA14_TestsProject_DoesNotReferenceForbiddenTestLibraries()
    {
        string[] forbidden = ["FluentAssertions", "AutoFixture", "Bogus", "Moq", "NSubstitute", "Testcontainers"];

        var offending = PackageReferences(TestsProject)
            .Where(reference => forbidden.Any(prefix =>
                reference.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                reference.Name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)))
            .Select(reference => reference.Name)
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void PLAN0001_CA14_TestsProject_HasNoArchitectureFolder()
    {
        Assert.False(Directory.Exists(Path.Combine(RepoRoot.Path, "tests", "Zentory.Tests", "Architecture")));
    }

    [Fact]
    public void PLAN0001_CA14_SrcProjects_DoNotDeclareInternalsVisibleTo()
    {
        var offending = SourceFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", "*.csproj")
            .Where(file => File.ReadAllText(file).Contains("InternalsVisibleTo", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void PLAN0001_CA34_ApiRuntimeClosure_KeepsEfCoreOn8032()
    {
        var closure = ApiRuntimeClosure();

        foreach (var package in new[]
                 {
                     "Microsoft.EntityFrameworkCore",
                     "Microsoft.EntityFrameworkCore.Relational",
                     "Microsoft.EntityFrameworkCore.Abstractions",
                     "Microsoft.EntityFrameworkCore.Design",
                 })
        {
            Assert.True(closure.TryGetValue(package, out var version), $"{package} no está en la clausura de Zentory.API.");
            Assert.Equal(EfCoreRuntimeVersion, version);
        }
    }

    [Fact]
    public void PLAN0001_CA34_ApiRuntimeClosure_HasNoPackageBelowBaseline()
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "Unit", "Build", "Fixtures", "api-runtime-baseline.json");
        var baseline = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baselinePath))!;
        var closure = ApiRuntimeClosure();

        var downgraded = baseline
            .Where(entry => closure.TryGetValue(entry.Key, out var current) &&
                            CompareVersions(current, entry.Value) < 0)
            .Select(entry => $"{entry.Key}: {entry.Value} -> {closure[entry.Key]}")
            .ToList();

        Assert.NotEmpty(baseline);
        Assert.Empty(downgraded);
    }

    // --- Apoyo ---------------------------------------------------------------------------------------------------

    private static IEnumerable<string> EnumerateProjectFiles()
        => new[] { "src", "tests" }
            .SelectMany(folder => SourceFiles(Path.Combine(RepoRoot.Path, folder), "*.csproj", "*.props", "*.targets"));

    // Excluye bin/ y obj/: ahí NuGet y MSBuild generan .props/.targets que no son del repo.
    private static IEnumerable<string> SourceFiles(string folder, params string[] patterns)
        => patterns
            .SelectMany(pattern => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
            .Where(file => !Relative(file).Split('/').Any(part => part is "bin" or "obj"));

    private static IEnumerable<XElement> Elements(string project, string name)
        => XDocument.Load(Path.Combine(RepoRoot.Path, project)).Descendants().Where(element => element.Name.LocalName == name);

    private static IEnumerable<(string Name, string? Version)> PackageReferences(string project)
        => Elements(project, "PackageReference")
            .Select(element => ((string?)element.Attribute("Include") ?? "", VersionOf(element)));

    private static string? VersionOf(XElement element)
        => (string?)element.Attribute("Version")
           ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value;

    private static Dictionary<string, string> ApiRuntimeClosure()
    {
        var assetsPath = Path.Combine(RepoRoot.Path, ApiAssetsFile);
        Assert.True(File.Exists(assetsPath),
            $"No existe {ApiAssetsFile}: ejecuta 'dotnet restore Zentory.slnx' antes de correr los tests.");

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var target = assets.RootElement.GetProperty("targets").GetProperty(ApiTargetFramework);

        var closure = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in target.EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;

            var separator = library.Name.LastIndexOf('/');
            closure[library.Name[..separator]] = library.Name[(separator + 1)..];
        }

        return closure;
    }

    // Compara la parte numérica; a igual número, una versión preliminar (con '-') es menor que la estable.
    private static int CompareVersions(string left, string right)
    {
        static (Version Number, bool Prerelease) Parse(string value)
        {
            var dash = value.IndexOf('-');
            var number = dash < 0 ? value : value[..dash];
            return (Version.Parse(number.Contains('.') ? number : number + ".0"), dash >= 0);
        }

        var (leftNumber, leftPre) = Parse(left);
        var (rightNumber, rightPre) = Parse(right);
        var byNumber = leftNumber.CompareTo(rightNumber);
        return byNumber != 0 ? byNumber : rightPre.CompareTo(leftPre);
    }

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');

    private static string Relative(string path) => NormalizeSeparators(Path.GetRelativePath(RepoRoot.Path, path));
}

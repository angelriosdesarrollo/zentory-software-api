using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Host;

// PLAN-0001 (RF-11, RF-23, RF-24): políticas estáticas del host verificadas sobre los archivos del repo.
// Nunca se lee appsettings.Development.json ni appsettings.Local.json: solo se consulta si git los versiona o ignora.
public sealed partial class HostConfigurationPolicyTests
{
    private const string ApiFolder           = "src/Zentory.API";
    private const string BaseSettingsFile    = ApiFolder + "/appsettings.json";
    private const string ProgramFile         = ApiFolder + "/Program.cs";
    private const string EnvironmentsFile    = ApiFolder + "/Extensions/HostEnvironmentExtensions.cs";


    // ── CA-22 ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0001_CA22_BaseAppSettings_DisablesInMemoryAndKeepsJwtKeysEmpty()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot.Path, BaseSettingsFile)));
        var root = settings.RootElement;

        Assert.Equal(JsonValueKind.False, root.GetProperty("Database").GetProperty("UseInMemory").ValueKind);
        Assert.Equal(string.Empty, root.GetProperty("Jwt").GetProperty("Key").GetString());
        Assert.Equal(string.Empty, root.GetProperty("Jwt").GetProperty("CollaboratorKey").GetString());
    }

    [Fact]
    public void PLAN0001_CA22_VersionedAppSettingsUnderSrc_AreOnlyBaseAndProduction()
    {
        var versioned = Git("ls-files", "--", "src/**/appsettings*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [ApiFolder + "/appsettings.Production.json", BaseSettingsFile],
            versioned);
    }

    [Fact]
    public void PLAN0001_CA22_LocalSecretFiles_AreNotVersionedAndStayIgnored()
    {
        var offending = Git("ls-files")
            .Where(path => LocalSecretFile().IsMatch(Path.GetFileName(path)))
            .ToList();
        Assert.Empty(offending);

        // Las reglas de .gitignore (líneas 38-40) siguen cubriendo los archivos locales del API.
        var ignored = Git("check-ignore", "--no-index",
            ApiFolder + "/appsettings.Development.json",
            ApiFolder + "/appsettings.Local.json",
            ApiFolder + "/appsettings.Staging.local.json");
        Assert.Equal(3, ignored.Count);
    }

    [Fact]
    public void PLAN0001_CA22_VersionedAppSettings_DoNotContainTestSigningKeys()
    {
        var testKeys = TestSigningKeys();
        Assert.NotEmpty(testKeys);

        var leaks = Git("ls-files", "--", "*appsettings*.json")
            .SelectMany(file =>
            {
                var content = File.ReadAllText(Path.Combine(RepoRoot.Path, file));
                return testKeys.Where(key => content.Contains(key, StringComparison.Ordinal))
                               .Select(_ => file);
            })
            .Distinct()
            .ToList();

        Assert.Empty(leaks);
    }

    // ── CA-T2-1 ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0001_CAT2_1_ProgramFile_EndsWithPublicPartialProgram()
    {
        var lastLine = File.ReadAllLines(Path.Combine(RepoRoot.Path, ProgramFile))
            .Select(line => line.Trim())
            .Last(line => line.Length > 0);

        Assert.Equal("public partial class Program;", lastLine);
    }

    [Fact]
    public void PLAN0001_CAT2_1_TestingLiteral_AppearsOnceInSrc_InAppEnvironments()
    {
        var occurrences = SourceFiles("src", "*.cs")
            .SelectMany(file => File.ReadAllLines(file)
                .Where(line => line.Contains("\"Testing\"", StringComparison.Ordinal))
                .Select(_ => Relative(file)))
            .ToList();

        Assert.Equal([EnvironmentsFile], occurrences);
    }

    [Fact]
    public void PLAN0001_CAT2_1_TestingEnvironmentConstant_IsUsedOnlyByRestrictingCode()
    {
        // RF-23: AppEnvironments.Testing solo restringe (fail-fast y exclusión de appsettings.Local.json).
        var users = SourceFiles("src", "*.cs")
            .Where(file => File.ReadAllText(file).Contains("AppEnvironments.Testing", StringComparison.Ordinal))
            .Select(Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.Equal([EnvironmentsFile, ProgramFile], users);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"^appsettings\.(Development|Local)\.json$|^appsettings\..+\.local\.json$", RegexOptions.IgnoreCase)]
    private static partial Regex LocalSecretFile();

    // Convención de las claves HMAC de los tests: literal "test-...signing-key...".
    [GeneratedRegex("\"(test-[^\"]*signing-key[^\"]*)\"")]
    private static partial Regex TestSigningKeyLiteral();

    // Las de TestJwtSettings (fuente única del host de pruebas) se leen de la clase, sigan o no la convención.
    private static List<string> TestSigningKeys()
        => SourceFiles(Path.Combine("tests", "Zentory.Tests"), "*.cs")
            .SelectMany(file => TestSigningKeyLiteral().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Concat([TestJwtSettings.Key, TestJwtSettings.CollaboratorKey])
            .Distinct()
            .ToList();

    private static List<string> Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory       = RepoRoot.Path,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Process.Start devolvió null.");
        }
        catch (Win32Exception exception)
        {
            Assert.Fail($"Este test necesita git en el PATH para leer los archivos versionados: {exception.Message}");
            throw;
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            // check-ignore sale con 1 cuando ningún archivo está ignorado; es un resultado, no un error.
            var acceptable = process.ExitCode == 0 || (arguments[0] == "check-ignore" && process.ExitCode == 1);
            Assert.True(acceptable, $"git {string.Join(' ', arguments)} falló ({process.ExitCode}): {error.Result}");

            return output.Result
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }

    // Excluye bin/ y obj/ (código generado que no es del repo).
    private static IEnumerable<string> SourceFiles(string folder, string pattern)
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, folder), pattern, SearchOption.AllDirectories)
            .Where(file => !Relative(file).Split('/').Any(part => part is "bin" or "obj"));

    private static string Relative(string file)
        => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/');
}

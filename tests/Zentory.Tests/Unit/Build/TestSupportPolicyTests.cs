using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using Zentory.Application.CollaboratorPortal;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 (RF-19, RNF-7; CA-25 b, e): políticas estáticas sobre tests/Zentory.Tests. TestJwtSettings es la única
// fuente de claves JWT de prueba, y todo doble o helper compartido vive en Support/ (o Security/Idor/). Los mensajes
// de fallo nombran archivo y línea, nunca el valor de una clave.
public sealed partial class TestSupportPolicyTests
{
    private const string TestsFolder      = "tests/Zentory.Tests";
    private const string JwtSettingsFile  = TestsFolder + "/Support/TestJwtSettings.cs";
    private const string PolicyFile       = TestsFolder + "/Unit/Build/TestSupportPolicyTests.cs";

    private static readonly string[] SharedHelperFolders = [TestsFolder + "/Support/", TestsFolder + "/Security/Idor/"];

    // ── CA-25 (b) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA25_TestsFolder_SinClavesJwtFueraDeTestJwtSettings()
    {
        var offending = TestSourceFiles()
            .Where(file => file != JwtSettingsFile && file != PolicyFile)
            .SelectMany(file => JwtKeyLiteralLines(File.ReadAllText(Path.Combine(RepoRoot.Path, file)))
                .Select(line => $"{file}:{line}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "Claves JWT de prueba fuera de TestJwtSettings: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA25_DetectorDeClavesJwt_EncuentraLasFormasConocidas()
    {
        // Control del anterior: las formas que tenían TestCollaboratorJwt y HostAuthenticationTests antes de RF-19.
        const string collaboratorJwtBefore = """
            ["Jwt:CollaboratorKey"] = "zentory-collaborator-key-0123456789abcdef",
            """;
        const string foreignKeyBefore = """
            private const string ForeignKey = "test-other-access-signing-key-0000000000000000";
            """;
        const string hostSetting = """
            .UseSetting("Jwt:Key", "some-local-key-with-enough-bytes-000000")
            """;
        const string inlineKey = """
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("inline-key-with-enough-bytes-0000000000"))
            """;
        const string fromSettings = """
            [TestJwtSettings.KeySetting] = TestJwtSettings.Key,
            """;

        Assert.NotEmpty(JwtKeyLiteralLines(collaboratorJwtBefore));
        Assert.NotEmpty(JwtKeyLiteralLines(foreignKeyBefore));
        Assert.NotEmpty(JwtKeyLiteralLines(hostSetting));
        Assert.NotEmpty(JwtKeyLiteralLines(inlineKey));
        Assert.Empty(JwtKeyLiteralLines(fromSettings));
    }

    [Fact]
    public void PLAN0018_CA25_TestCollaboratorJwt_FirmaConClaveIssuerYAudienceDeTestJwtSettings()
    {
        var collaboratorId = Guid.NewGuid();
        var token = TestCollaboratorJwt.CreateService().GenerateSessionToken(
            "collaborator@zentory.test", [collaboratorId], collaboratorId, Guid.NewGuid(), "Org de prueba",
            CollaboratorAuthMethod.MagicLink);

        var parameters = new TokenValidationParameters
        {
            ValidIssuer      = TestJwtSettings.Issuer,
            ValidAudience    = TestJwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSettings.CollaboratorKey)),
        };
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out var validated);

        Assert.NotNull(principal);
        Assert.Equal(TestJwtSettings.Issuer, validated.Issuer);
    }

    // ── CA-25 (e) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA25_TestsFolder_SinDoblesNiHelpersCompartidosFueraDeSupport()
    {
        var offending = TestSourceFiles()
            .Where(file => file != PolicyFile)
            .Where(file => !SharedHelperFolders.Any(folder => file.StartsWith(folder, StringComparison.Ordinal)))
            .SelectMany(file => SharedHelperTypes(File.ReadAllText(Path.Combine(RepoRoot.Path, file)))
                .Select(type => $"{file}: {type}"))
            .ToList();

        Assert.True(offending.Count == 0,
            "Dobles o helpers compartidos fuera de Support/ y Security/Idor/: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA25_DetectorDeHelpers_EncuentraFakeBuilderRecorderYFactory()
    {
        // Control del anterior: incluye la forma que tenía ConnectionOpenRecorder dentro de PostgresModelTests.
        const string sample = """
            private sealed class ConnectionOpenRecorder : DbConnectionInterceptor { }
            public sealed class FakeClock { }
            internal sealed record TenantBuilder(Guid Id);
            public class CustomApiFactory : ZentoryApiFactory { }
            public sealed class FactoryIsolationTests { }
            public sealed class BuilderTests { }
            """;

        Assert.Equal(["ConnectionOpenRecorder", "FakeClock", "TenantBuilder", "CustomApiFactory"], SharedHelperTypes(sample));
    }

    [Fact]
    public void PLAN0018_CA25_ConnectionOpenRecorder_ViveEnSupport()
    {
        Assert.Equal("Zentory.Tests.Support", typeof(ConnectionOpenRecorder).Namespace);
        Assert.True(File.Exists(Path.Combine(RepoRoot.Path, TestsFolder, "Support", "ConnectionOpenRecorder.cs")));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Literal de clave HMAC con la convención de los tests (letras, dígitos y guiones alrededor de "signing-key"), valor
    // literal asignado a una clave de configuración JWT, o literal dentro de un SymmetricSecurityKey. Este archivo se
    // excluye de los escaneos porque sus controles contienen esas formas a propósito.
    [GeneratedRegex(@"""[A-Za-z0-9_-]*signing-key[A-Za-z0-9_-]*""")]
    private static partial Regex SigningKeyLiteral();

    [GeneratedRegex("\"Jwt:(Key|CollaboratorKey)\"\\s*(\\]\\s*=|,)\\s*\"")]
    private static partial Regex JwtSettingLiteral();

    [GeneratedRegex("SymmetricSecurityKey\\([^;]*\"")]
    private static partial Regex InlineSymmetricKey();

    // Declaraciones de tipo Fake*, *Builder, *Recorder o *Factory (incluidas las anidadas y los record).
    [GeneratedRegex(@"\b(?:class|record|struct|interface)\s+(?:class\s+|struct\s+)?(Fake\w*|\w+(?:Builder|Recorder|Factory))\b")]
    private static partial Regex SharedHelperDeclaration();

    private static List<int> JwtKeyLiteralLines(string content)
        => content.Split('\n')
            .Select((line, index) => (line, number: index + 1))
            .Where(item => SigningKeyLiteral().IsMatch(item.line)
                        || JwtSettingLiteral().IsMatch(item.line)
                        || InlineSymmetricKey().IsMatch(item.line)
                        || item.line.Contains(TestJwtSettings.Key, StringComparison.Ordinal)
                        || item.line.Contains(TestJwtSettings.CollaboratorKey, StringComparison.Ordinal)
                        || item.line.Contains(TestJwtSettings.ForeignKey, StringComparison.Ordinal))
            .Select(item => item.number)
            .ToList();

    private static List<string> SharedHelperTypes(string content)
        => SharedHelperDeclaration().Matches(content).Select(match => match.Groups[1].Value).ToList();

    // Archivos .cs de tests/Zentory.Tests, relativos al repo y sin bin/ ni obj/.
    private static IEnumerable<string> TestSourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "tests", "Zentory.Tests"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"));
}

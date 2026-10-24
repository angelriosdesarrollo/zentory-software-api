using System.Text;
using System.Text.Json;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0001 (RF-13, SR-0001.7): las claves JWT de prueba son seguras para HMAC-SHA256, propias de los tests y nunca
// coinciden con las de configuración real. Los appsettings locales (no versionados) solo se comparan: ningún valor
// suyo se imprime ni aparece en un mensaje de aserción.
public sealed class TestJwtSettingsTests
{
    private const int MinimumHmacKeyBytes = 32;

    private static readonly string[] LocalSettingsFiles = ["appsettings.Development.json", "appsettings.Local.json"];

    private static readonly string ApiFolder = Path.Combine(RepoRoot.Path, "src", "Zentory.API");

    [Fact]
    public void PLAN0001_CA31_TestJwtKeys_AreAtLeast32BytesAndDistinct()
    {
        Assert.True(Encoding.UTF8.GetByteCount(TestJwtSettings.Key) >= MinimumHmacKeyBytes);
        Assert.True(Encoding.UTF8.GetByteCount(TestJwtSettings.CollaboratorKey) >= MinimumHmacKeyBytes);
        Assert.NotEqual(TestJwtSettings.Key, TestJwtSettings.CollaboratorKey);
    }

    [Fact]
    public void PLAN0001_CA31_TestIssuerAndAudience_DifferFromBaseAppSettings()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(ApiFolder, "appsettings.json")));
        var jwt = settings.RootElement.GetProperty("Jwt");

        var baseIssuer   = jwt.GetProperty("Issuer").GetString();
        var baseAudience = jwt.GetProperty("Audience").GetString();
        Assert.Equal("zentory-api", baseIssuer);
        Assert.Equal("zentory-app", baseAudience);

        Assert.NotEqual(TestJwtSettings.Issuer, baseIssuer);
        Assert.NotEqual(TestJwtSettings.Audience, baseAudience);
    }

    [Fact]
    public void PLAN0001_CA31_TestJwtKeys_DoNotAppearInAnySourceFile()
    {
        // Incluye los appsettings locales si existen: la comprobación es booleana y solo se reporta la ruta.
        var leaks = SourceTextFiles()
            .Where(file =>
            {
                var content = File.ReadAllText(file);
                return content.Contains(TestJwtSettings.Key, StringComparison.Ordinal)
                    || content.Contains(TestJwtSettings.CollaboratorKey, StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .ToList();

        Assert.Empty(leaks);
    }

    [Fact]
    public void PLAN0001_CA31_TestJwtKeys_DifferFromLocalAppSettingsKeys_WithoutPrintingThem()
    {
        // Si los archivos locales no existen (CI, otra máquina), no hay nada que comparar.
        foreach (var fileName in LocalSettingsFiles)
        {
            var path = Path.Combine(ApiFolder, fileName);
            if (!File.Exists(path)) continue;

            var localKeys = ReadLocalJwtKeys(path);
            foreach (var localKey in localKeys)
            {
                // Assert.True/False con mensaje genérico: Assert.NotEqual imprimiría el valor al fallar.
                Assert.False(string.Equals(localKey, TestJwtSettings.Key, StringComparison.Ordinal),
                    $"Una clave JWT de {fileName} coincide con TestJwtSettings.Key.");
                Assert.False(string.Equals(localKey, TestJwtSettings.CollaboratorKey, StringComparison.Ordinal),
                    $"Una clave JWT de {fileName} coincide con TestJwtSettings.CollaboratorKey.");
            }
        }
    }

    // Jwt:Key y Jwt:CollaboratorKey del archivo local, sin exponerlos. Un archivo ilegible se reporta solo por nombre.
    private static List<string> ReadLocalJwtKeys(string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            Assert.Fail($"{Path.GetFileName(path)} no es JSON válido; no se pudo comparar sus claves JWT.");
            throw;
        }

        using (document)
        {
            var keys = new List<string>();
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Jwt", out var jwt)
                && jwt.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "Key", "CollaboratorKey" })
                {
                    if (jwt.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrEmpty(value.GetString()))
                        keys.Add(value.GetString()!);
                }
            }

            return keys;
        }
    }

    // Archivos de texto de src/ (sin bin/, obj/ ni el almacenamiento local de desarrollo, que guarda binarios).
    private static IEnumerable<string> SourceTextFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var parts = Path.GetRelativePath(RepoRoot.Path, file).Split('\\', '/');
                return !parts.Any(part => part is "bin" or "obj" or "App_Data")
                    && !string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase);
            });
}

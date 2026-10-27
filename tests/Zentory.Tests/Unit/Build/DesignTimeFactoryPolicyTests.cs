using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 (RF-2, SR-0018.3; ADR-0007 §2, §3): la fábrica de diseño usa una cadena marcador sin credenciales y no lee
// configuración ni entorno, y la herramienta dotnet-ef del manifiesto local está fijada a la versión de EF Design de
// Infrastructure. Los mensajes nombran la regla, nunca el contenido de una cadena.
public sealed class DesignTimeFactoryPolicyTests
{
    private const string FactoryFile           = "src/Zentory.Infrastructure/Persistence/ZentoryDbContextFactory.cs";
    private const string ToolManifestFile      = ".config/dotnet-tools.json";
    private const string InfrastructureProject = "src/Zentory.Infrastructure/Zentory.Infrastructure.csproj";
    private const string DesignPackage         = "Microsoft.EntityFrameworkCore.Design";
    private const string EfTool                = "dotnet-ef";

    private static readonly string[] CredentialKeys = ["Username", "User Id", "UserId", "Password", "Pwd"];

    // Formas de leer configuración o entorno que la fábrica no puede usar.
    private static readonly string[] ForbiddenReads =
    [
        "IConfiguration", "ConfigurationBuilder", "AddJsonFile", "AddEnvironmentVariables", "Environment", "appsettings",
    ];

    [Fact]
    public void PLAN0018_CA64_FabricaDeDiseno_CadenaSinCredenciales()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Path, FactoryFile));
        var offendingKeys = CredentialKeys
            .Where(key => source.Contains(key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offendingKeys.Count == 0,
            $"{FactoryFile} menciona claves de credenciales: " + string.Join(", ", offendingKeys));

        // La cadena efectiva del contexto que crea la herramienta tampoco trae usuario ni contraseña.
        using var db = new ZentoryDbContextFactory().CreateDbContext([]);
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());

        Assert.True(string.IsNullOrEmpty(connection.Username), "La cadena de diseño trae usuario.");
        Assert.True(string.IsNullOrEmpty(connection.Password), "La cadena de diseño trae contraseña.");
        Assert.Equal("design-time-only", connection.Host);
        Assert.Equal(ZentoryDbContextFactory.DesignTimeConnectionString, db.Database.GetConnectionString());
    }

    [Fact]
    public void PLAN0018_CA64_FabricaDeDiseno_NoLeeConfiguracionNiEntorno()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Path, FactoryFile));
        var offending = ForbiddenReads
            .Where(token => Regex.IsMatch(source, @"\b" + Regex.Escape(token) + @"\b", RegexOptions.IgnoreCase))
            .ToList();

        Assert.True(offending.Count == 0, $"{FactoryFile} lee configuración o entorno: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA64_ManifiestoDeHerramientas_DotnetEfExactoIgualAEfDesignDeInfrastructure()
    {
        var manifestPath = Path.Combine(RepoRoot.Path, ToolManifestFile);
        Assert.True(File.Exists(manifestPath), $"Falta {ToolManifestFile}.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.True(manifest.RootElement.GetProperty("tools").TryGetProperty(EfTool, out var tool),
            $"{ToolManifestFile} no declara {EfTool}.");
        var toolVersion = tool.GetProperty("version").GetString();

        var designVersions = XDocument.Load(Path.Combine(RepoRoot.Path, InfrastructureProject))
            .Descendants("PackageReference")
            .Where(reference => string.Equals((string?)reference.Attribute("Include"), DesignPackage,
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => (string?)reference.Attribute("Version"))
            .ToList();
        var designVersion = Assert.Single(designVersions);

        // Versión exacta: tres números, sin comodines ni rangos.
        Assert.Matches(@"^\d+\.\d+\.\d+$", toolVersion ?? string.Empty);
        Assert.Equal(designVersion, toolVersion);
    }
}

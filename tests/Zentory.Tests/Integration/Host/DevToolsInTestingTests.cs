using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0001 (RF-18 g, RF-23; SR-0001.6): el ambiente Testing solo restringe. Ninguna herramienta de desarrollo
// (endpoints dev, Swagger, dashboard de Hangfire) queda expuesta en el host de pruebas, y la constante Testing no
// condiciona nada fuera del fail-fast y de la carga de appsettings.Local.json.
public sealed class DevToolsInTestingTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string TestingConstantDefinition = "public const string Testing = \"Testing\";";
    private const string TestingConstantUsage      = "AppEnvironments.Testing";

    public static TheoryData<string, string> DevTools => new()
    {
        { "POST", "/api/v1/public/dev/seed-magic-link-token" },
        { "GET",  "/api/v1/public/dev/local-storage/x" },
        { "GET",  "/swagger/index.html" },
        { "GET",  "/hangfire" },
    };

    [Theory]
    [MemberData(nameof(DevTools))]
    public async Task PLAN0001_CA30_Testing_HerramientasDev_Devuelven404(string method, string path)
    {
        // Sin seguir redirecciones: se mide la respuesta del host tal cual (UseHttpsRedirection está activo fuera de
        // Development).
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void PLAN0001_CA30_Testing_LiteralYConstante_SoloEnFailFastYAppsettingsLocal()
    {
        var lines = SourceLines().ToList();

        // El literal "Testing" aparece una sola vez: la definición de AppEnvironments.Testing.
        var literal = lines.Where(l => l.Text.Contains("\"Testing\"", StringComparison.Ordinal)).ToList();
        var definition = Assert.Single(literal);
        Assert.Equal("src/Zentory.API/Extensions/HostEnvironmentExtensions.cs", definition.File);
        Assert.Equal(TestingConstantDefinition, definition.Text.Trim());

        // La constante solo se usa en el fail-fast (HostEnvironmentExtensions) y en Program.cs para no cargar
        // appsettings.Local.json.
        var usages = lines.Where(l => l.Text.Contains(TestingConstantUsage, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(usages);
        Assert.All(usages, l => Assert.Contains(l.File,
            new[] { "src/Zentory.API/Extensions/HostEnvironmentExtensions.cs", "src/Zentory.API/Program.cs" }));

        var guardUsages = usages.Where(l => l.File.EndsWith("HostEnvironmentExtensions.cs", StringComparison.Ordinal));
        Assert.All(guardUsages, l => Assert.True(l.Index > IndexOf(lines, l.File, "EnsureSafeDatabaseConfiguration"),
            $"Uso de {TestingConstantUsage} fuera del fail-fast: {l.Text.Trim()}"));

        var programUsages = usages.Where(l => l.File == "src/Zentory.API/Program.cs").ToList();
        var programUsage = Assert.Single(programUsages);
        var program = lines.Where(l => l.File == "src/Zentory.API/Program.cs").ToList();
        var next = program.First(l => l.Index == programUsage.Index + 1);
        Assert.Contains("AddJsonFile(\"appsettings.Local.json\"", next.Text);
    }

    private static int IndexOf(IEnumerable<SourceLine> lines, string file, string marker)
        => lines.First(l => l.File == file && l.Text.Contains(marker, StringComparison.Ordinal)).Index;

    private static IEnumerable<SourceLine> SourceLines()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"))
            .SelectMany(file => File.ReadAllLines(Path.Combine(RepoRoot.Path, file))
                .Select((text, index) => new SourceLine(file, index, text)));

    private sealed record SourceLine(string File, int Index, string Text);
}

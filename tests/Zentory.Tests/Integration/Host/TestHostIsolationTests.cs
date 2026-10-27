using System.Runtime.CompilerServices;
using Anthropic;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resend;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 (RF-19; CA-25 a, c, d): los hosts de prueba no usan la configuración de la máquina ni dejan rastros en el
// repo o en %TEMP%. Ningún valor de entorno real se imprime: las aserciones solo nombran la clave.
[Collection(NonParallelCollection.Name)]
public sealed class TestHostIsolationTests
{
    // Variables trampa: cada una con un valor único que no puede venir de ningún appsettings.
    private static readonly IReadOnlyDictionary<string, string> TrapVariables = new Dictionary<string, string>
    {
        ["ConnectionStrings__DefaultConnection"] = "Host=trap-db.invalid;Database=trap;Username=trap;Password=trap-pass",
        ["R2__AccountId"]                        = "trap-r2-account",
        ["Resend__ApiKey"]                       = "trap-resend-api-key",
        ["Anthropic__ApiKey"]                    = "trap-anthropic-api-key",
        ["DemoData__Password"]                   = "trap-demo-password",
        ["App__BaseUrl"]                         = "https://trap-app.invalid",
        ["Cors__AllowedOrigins"]                 = "https://trap-cors.invalid",
        // Comodines de las secciones neutralizadas (ConnectionStrings__*, R2__*, Cors__*).
        ["ConnectionStrings__Reporting"]         = "Host=trap-reporting.invalid;Password=trap-pass",
        ["CUSTOMCONNSTR_Legacy"]                 = "Host=trap-legacy.invalid;Password=trap-pass",
        ["R2__SecretKey"]                        = "trap-r2-secret",
        ["Cors__ExposedHeaders"]                 = "trap-cors-header",
    };

    // ── CA-25 (a) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA25_MinimalHostFactory_ConVariablesDeMaquinaEnElProceso_NoUsaSusValores()
    {
        await WithTrapEnvironmentAsync(async () =>
        {
            await using var factory = new MinimalHostFactory(Environments.Development, new Dictionary<string, string?>
            {
                [DatabaseSettings.UseInMemoryKey]   = "true",
                [DatabaseSettings.InMemoryNameKey]  = $"zentory-isolation-{Guid.NewGuid():N}",
                [HangfireSettings.ServerEnabledKey] = "false",
                [DevSeedSettings.EnabledKey]        = "false",
            });
            using var client = factory.CreateClient();

            AssertHostIgnoresTrapValues(factory.Services);
        });
    }

    [Fact]
    public async Task PLAN0018_CA25_ZentoryApiFactory_ConVariablesDeMaquinaEnElProceso_NoUsaSusValores()
    {
        await WithTrapEnvironmentAsync(async () =>
        {
            await using var factory = new ZentoryApiFactory();
            using var client = factory.CreateClient();

            AssertHostIgnoresTrapValues(factory.Services);
        });
    }

    [Fact]
    public async Task PLAN0018_CA25_MinimalHostFactory_SinAjustesJwt_ParteDeTestJwtSettings()
    {
        await using var factory = new MinimalHostFactory(Environments.Development, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-isolation-jwt-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
        });
        using var client = factory.CreateClient();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        foreach (var (key, value) in TestJwtSettings.AsSettings())
            Assert.True(string.Equals(value, configuration[key], StringComparison.Ordinal),
                $"{key} no sale de TestJwtSettings.");
    }

    // ── CA-25 (c) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA25_Suite_TrasCorrer_NoDejaArchivosNuevosBajoAppDataDelApi()
    {
        // ZentoryApiFactory: raíz de contenido temporal y almacenamiento en memoria.
        await using (var factory = new ZentoryApiFactory())
        {
            using var client = factory.CreateClient();
            var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
            Assert.Equal(factory.ContentRootPath, environment.ContentRootPath);
            Assert.False(IsUnder(environment.ContentRootPath, AppDataBaseline.ApiFolder),
                "La raíz de contenido de ZentoryApiFactory está dentro de src/Zentory.API.");

            using var scope = factory.Services.CreateScope();
            var storage = Assert.IsType<FakeStorageService>(scope.ServiceProvider.GetRequiredService<IStorageService>());
            await using var content = new MemoryStream([1, 2, 3]);
            await storage.UploadAsync("tests/ca25/fake.bin", content, "application/octet-stream");
            Assert.Equal([1, 2, 3], storage.Objects["tests/ca25/fake.bin"].Content);
        }

        // MinimalHostFactory: sin doble de almacenamiento, el disco local cae en su raíz temporal y se borra con ella.
        string minimalRoot;
        await using (var factory = new MinimalHostFactory(Environments.Development, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-isolation-storage-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
        }))
        {
            using var client = factory.CreateClient();
            minimalRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            Assert.Equal(factory.ContentRootPath, minimalRoot);
            Assert.False(IsUnder(minimalRoot, AppDataBaseline.ApiFolder),
                "La raíz de contenido de MinimalHostFactory está dentro de src/Zentory.API.");

            using var scope = factory.Services.CreateScope();
            var storage = Assert.IsType<LocalDiskStorageService>(scope.ServiceProvider.GetRequiredService<IStorageService>());
            await using var content = new MemoryStream([4, 5, 6]);
            await storage.UploadAsync("tests/ca25/local.bin", content, "application/octet-stream");
            Assert.True(File.Exists(Path.Combine(minimalRoot, "App_Data", "dev-storage", "tests", "ca25", "local.bin")));
        }
        Assert.False(Directory.Exists(minimalRoot), "La raíz temporal de MinimalHostFactory no se borró.");

        // Corre en la colección sin paralelismo, que xUnit ejecuta después de las paralelas: lo que haya escrito la
        // suite hasta aquí ya está en disco. Solo se comparan rutas, nunca contenidos.
        var created = AppDataBaseline.Current().Except(AppDataBaseline.AtStart, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(created.Count == 0,
            "La suite dejó archivos nuevos bajo src/Zentory.API/App_Data: " + string.Join(", ", created));
    }

    // ── CA-25 (d) ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA25_VersionedContentRoot_CopiaFallida_NoDejaCarpetaTemporal()
    {
        var prefix = $"zentory-host-ca25-{Guid.NewGuid():N}-";
        // Origen con solo appsettings.json: la primera copia funciona y la segunda falla, así la carpeta temporal ya
        // tiene contenido cuando hay que borrarla.
        var source = Directory.CreateTempSubdirectory("zentory-ca25-source-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(source, "appsettings.json"), "{}");

            var error = Record.Exception(() => new VersionedContentRoot(source, prefix));

            Assert.IsType<FileNotFoundException>(error);
            Assert.Empty(TempDirectories(prefix));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void PLAN0018_CA25_VersionedContentRoot_CopiaCorrecta_CreaYBorraSuCarpeta()
    {
        // Control del anterior: con el origen real la carpeta existe con los dos appsettings, y Dispose la borra.
        var prefix = $"zentory-host-ca25-ok-{Guid.NewGuid():N}-";
        var root = new VersionedContentRoot(AppDataBaseline.ApiFolder, prefix);
        Assert.Single(TempDirectories(prefix));
        Assert.True(File.Exists(Path.Combine(root.Path, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(root.Path, "appsettings.Production.json")));

        root.Dispose();

        Assert.Empty(TempDirectories(prefix));
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static void AssertHostIgnoresTrapValues(IServiceProvider services)
    {
        var configuration = (IConfigurationRoot)services.GetRequiredService<IConfiguration>();

        // Control: las variables sí llegan a los proveedores del host; es la factory la que las neutraliza.
        Assert.True(configuration.Providers.Any(p =>
                p.TryGet("R2:AccountId", out var value) && value == TrapVariables["R2__AccountId"]),
            "El proveedor de variables de entorno no recibió la variable trampa.");

        foreach (var (variable, trap) in TrapVariables)
        {
            var key = variable.StartsWith("CUSTOMCONNSTR_", StringComparison.Ordinal)
                ? "ConnectionStrings:" + variable["CUSTOMCONNSTR_".Length..]
                : variable.Replace("__", ":", StringComparison.Ordinal);
            Assert.False(string.Equals(configuration[key], trap, StringComparison.Ordinal),
                $"El host usa el valor de entorno de {key}.");
        }

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.False(provider.GetRequiredService<IStorageService>() is CloudflareR2StorageService,
            "El almacenamiento es R2 por la variable de entorno.");
        Assert.False(string.Equals(provider.GetRequiredService<IApplicationSettings>().BaseUrl,
            TrapVariables["App__BaseUrl"], StringComparison.OrdinalIgnoreCase), "App:BaseUrl sale del entorno.");
        Assert.False(string.Equals(provider.GetRequiredService<IOptions<ResendClientOptions>>().Value.ApiToken,
            TrapVariables["Resend__ApiKey"], StringComparison.Ordinal), "Resend:ApiKey sale del entorno.");
        Assert.False(string.Equals(provider.GetRequiredService<AnthropicClient>().ApiKey,
            TrapVariables["Anthropic__ApiKey"], StringComparison.Ordinal), "Anthropic:ApiKey sale del entorno.");

        var cors = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("Frontend");
        Assert.NotNull(cors);
        Assert.DoesNotContain(cors!.Origins, origin =>
            string.Equals(origin, TrapVariables["Cors__AllowedOrigins"], StringComparison.OrdinalIgnoreCase));
    }

    // Fija las variables trampa y restaura después las del proceso. Los valores previos se guardan sin mostrarlos.
    private static async Task WithTrapEnvironmentAsync(Func<Task> action)
    {
        var previous = TrapVariables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (name, value) in TrapVariables)
                Environment.SetEnvironmentVariable(name, value);

            await action();
        }
        finally
        {
            foreach (var (name, value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static List<string> TempDirectories(string prefix)
        => Directory.EnumerateDirectories(Path.GetTempPath(), prefix + "*").ToList();

    private static bool IsUnder(string path, string folder)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}

// Rutas bajo src/Zentory.API/App_Data al cargar el ensamblado de tests, antes de que corra ningún test (CA-25 c).
internal static class AppDataBaseline
{
    public static readonly string ApiFolder = Path.Combine(RepoRoot.Path, "src", "Zentory.API");

    private static readonly string AppDataFolder = Path.Combine(ApiFolder, "App_Data");

    public static IReadOnlyCollection<string> AtStart { get; private set; } = [];

#pragma warning disable CA2255 // Inicializador de módulo en un ensamblado de tests: única forma de fotografiar antes de la suite.
    [ModuleInitializer]
    internal static void Capture() => AtStart = Current();
#pragma warning restore CA2255

    public static IReadOnlyCollection<string> Current()
        => Directory.Exists(AppDataFolder)
            ? Directory.EnumerateFiles(AppDataFolder, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(AppDataFolder, file).Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
}

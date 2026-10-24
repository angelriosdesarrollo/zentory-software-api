using System.Reflection;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Extensions;
using Zentory.Application.Common.Interfaces;
using Zentory.Domain.Constants;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Services;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0001 (RF-12, RF-22, RF-23; D-1, D-2; R-6, R-7): la ZentoryApiFactory aísla el host de pruebas del entorno de la
// máquina y de las demás instancias. Ninguna aserción de aislamiento se apoya en los filtros globales de EF (H-08 es de
// la posición 1): las lecturas de comprobación usan IgnoreQueryFilters().

// Modifica variables de entorno del proceso: corre sin paralelismo.
[Collection(NonParallelCollection.Name)]
public sealed class FactoryEnvironmentIsolationTests
{
    private const string ConnectionStringVariable = "ConnectionStrings__DefaultConnection";
    private const string R2AccountVariable        = "R2__AccountId";

    // Valores de entorno trampa: apuntan a un Postgres y a una cuenta R2 que no existen.
    private const string PostgresConnectionString =
        "Host=postgres.factory-test.invalid;Port=5432;Database=zentory;Username=zentory;Password=factory-test-password";
    private const string R2AccountId = "factory-test-r2-account";

    [Fact]
    public async Task PLAN0001_CA27_Factory_WithPostgresAndR2InProcessEnvironment_StaysInMemoryWithoutR2OrLocalJson()
    {
        var previousConnection = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        var previousR2Account  = Environment.GetEnvironmentVariable(R2AccountVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, PostgresConnectionString);
            Environment.SetEnvironmentVariable(R2AccountVariable, R2AccountId);

            await using var factory = new ZentoryApiFactory();
            using var client = factory.CreateClient();
            using var scope = factory.Services.CreateScope();
            var provider = scope.ServiceProvider;

            // Control: las variables sí llegan a la configuración del host; es la factory la que las neutraliza.
            var configuration = (IConfigurationRoot)provider.GetRequiredService<IConfiguration>();
            Assert.Contains(configuration.Providers, p =>
                p.TryGet("R2:AccountId", out var value) && value == R2AccountId);

            // (a) Base InMemory con el nombre propio de esta instancia.
            var db = provider.GetRequiredService<ZentoryDbContext>();
            Assert.True(db.Database.IsInMemory());
            Assert.Equal(factory.DatabaseName, InMemoryStoreName(db));

            // (b) JobStorage leído del contenedor, no JobStorage.Current (global del proceso, R-6).
            var jobStorage = provider.GetRequiredService<JobStorage>();
            Assert.False(jobStorage is PostgreSqlStorage, $"JobStorage es {jobStorage.GetType().FullName}.");

            // (c) Sin R2.
            var storage = provider.GetRequiredService<IStorageService>();
            Assert.False(storage is CloudflareR2StorageService, $"IStorageService es {storage.GetType().FullName}.");

            // (d) appsettings.Local.json no es fuente de configuración del host (RF-22).
            var localJsonProviders = configuration.Providers
                .OfType<JsonConfigurationProvider>()
                .Where(p => string.Equals(Path.GetFileName(p.Source.Path), "appsettings.Local.json",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Empty(localJsonProviders);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, previousConnection);
            Environment.SetEnvironmentVariable(R2AccountVariable, previousR2Account);
        }

        Assert.Equal(previousConnection, Environment.GetEnvironmentVariable(ConnectionStringVariable));
        Assert.Equal(previousR2Account, Environment.GetEnvironmentVariable(R2AccountVariable));
    }

    [Fact]
    public async Task PLAN0001_CA21_Factory_WithInMemoryInProduction_FailsBeforeRegisteringServices()
    {
        // Refuerzo de CA-21: ConfigureTestServices corre dentro de builder.Build(); si el fail-fast estuviera después
        // de Build, el centinela quedaría en true. La raíz de contenido es una VersionedContentRoot: en Production,
        // Program.cs cargaría el appsettings.Local.json de la máquina.
        using var contentRoot = new VersionedContentRoot();
        await using var factory = new ZentoryApiFactory();
        await using var production = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Production")
            .UseContentRoot(contentRoot.Path));

        var startupError = Record.Exception(() => production.CreateClient());

        Assert.NotNull(startupError);
        Assert.True(ContainsGuardError(startupError), $"El arranque no falló con el fail-fast: {startupError}");
        Assert.False(factory.TestServicesConfigured, "Se registraron servicios antes del fail-fast de UseInMemory.");

        // Control: en Testing el mismo centinela sí se activa.
        await using var testing = new ZentoryApiFactory();
        using var client = testing.CreateClient();
        Assert.True(testing.TestServicesConfigured);
    }

    private static bool ContainsGuardError(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException && current.Message.Contains("Database:UseInMemory", StringComparison.Ordinal))
                return true;
            if (current is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsGuardError))
                return true;
        }

        return false;
    }

#pragma warning disable EF1001 // API interna de EF: única forma de leer el nombre de la base InMemory del contexto.
    internal static string? InMemoryStoreName(DbContext db)
        => db.GetService<IDbContextOptions>().FindExtension<InMemoryOptionsExtension>()?.StoreName;
#pragma warning restore EF1001
}

public sealed class FactoryIsolationTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private static readonly Assembly[] SourceAssemblies =
    [
        typeof(Program).Assembly,                 // Zentory.API
        typeof(IEmailService).Assembly,           // Zentory.Application
        typeof(ZentoryDbContext).Assembly,        // Zentory.Infrastructure
        typeof(Organization).Assembly,            // Zentory.Domain
    ];

    private static readonly string[] DoubleNamePrefixes = ["Fake", "Stub", "Mock", "Dummy", "Spy"];

    [Fact]
    public void PLAN0001_CA29_Factory_ResolvesAiAndEmail_AsSupportDoubles()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<FakeAiTextGenerationService>(scope.ServiceProvider.GetRequiredService<IAiTextGenerationService>());
        Assert.IsType<FakeEmailService>(scope.ServiceProvider.GetRequiredService<IEmailService>());
        Assert.Equal(typeof(ZentoryApiFactory).Assembly, typeof(FakeAiTextGenerationService).Assembly);
        Assert.Equal(typeof(ZentoryApiFactory).Assembly, typeof(FakeEmailService).Assembly);
    }

    [Fact]
    public void PLAN0001_CA29_SourceAssemblies_ContainNoTestDoubles()
    {
        var types = SourceAssemblies.SelectMany(LoadableTypes).ToList();

        var doubles = types
            .Where(t => DoubleNamePrefixes.Any(prefix => t.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(doubles);

        // Las únicas implementaciones de IA y email en src/ son las reales.
        Assert.Equal([typeof(AnthropicTextGenerationService)], Implementations<IAiTextGenerationService>(types));
        Assert.Equal([typeof(ResendEmailService)], Implementations<IEmailService>(types));
    }

    [Fact]
    public void PLAN0001_CA29_SourceFiles_HaveNoServiceRegistrationConditionedOnTesting()
    {
        // RF-23: el ambiente Testing solo restringe. Toda consulta de ambiente con nombre arbitrario (IsEnvironment)
        // vive en el fail-fast y en Program.cs, y en Program.cs lo único que condiciona es appsettings.Local.json.
        var environmentChecks = SourceLines()
            .Where(l => l.Text.Contains("IsEnvironment(", StringComparison.Ordinal))
            .ToList();

        Assert.All(environmentChecks, l => Assert.Contains("AppEnvironments.Testing", l.Text));
        Assert.Equal(
            ["src/Zentory.API/Extensions/HostEnvironmentExtensions.cs", "src/Zentory.API/Program.cs"],
            environmentChecks.Select(l => l.File).Distinct().OrderBy(f => f, StringComparer.Ordinal));

        var program = File.ReadAllLines(Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Program.cs"));
        var guarded = Array.FindIndex(program, line => line.Contains("IsEnvironment(", StringComparison.Ordinal));
        Assert.Contains("AddJsonFile(\"appsettings.Local.json\"", program[guarded + 1]);

        var testingLines = SourceLines()
            .Where(l => l.Text.Contains("AppEnvironments.Testing", StringComparison.Ordinal)
                     || l.Text.Contains("\"Testing\"", StringComparison.Ordinal))
            .ToList();
        Assert.DoesNotContain(testingLines, l => l.Text.Contains("Services.", StringComparison.Ordinal)
                                              || l.Text.Contains("services.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PLAN0001_CA36_TwoFactories_OrganizationCreatedInFirst_IsNotVisibleInSecond()
    {
        await using var first = new ZentoryApiFactory();
        await using var second = new ZentoryApiFactory();
        Assert.NotEqual(first.DatabaseName, second.DatabaseName);

        var organization = Organization.Create($"Factory isolation {Guid.NewGuid():N}", LegalType.Freelance);
        using (var scope = first.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            db.Organizations.Add(organization);
            await db.SaveChangesAsync();
        }

        using (var scope = first.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            Assert.True(await db.Organizations.IgnoreQueryFilters()
                .AnyAsync(o => o.OrganizationId == organization.OrganizationId));
        }

        using (var scope = second.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            Assert.Equal(second.DatabaseName, FactoryEnvironmentIsolationTests.InMemoryStoreName(db));
            Assert.False(await db.Organizations.IgnoreQueryFilters()
                .AnyAsync(o => o.OrganizationId == organization.OrganizationId));
        }
    }

    private static List<Type> Implementations<TService>(IEnumerable<Type> types)
        => types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(TService).IsAssignableFrom(t)).ToList();

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
    }

    private static IEnumerable<(string File, string Text)> SourceLines()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"))
            .SelectMany(file => File.ReadAllLines(Path.Combine(RepoRoot.Path, file)).Select(text => (file, text)));
}

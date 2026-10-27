using System.Reflection;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 (RF-55; CA-66, parte de T22): la API declara UserSecretsId y solo Development carga user-secrets. Los tests
// inspeccionan las fuentes de configuración registradas, nunca sus valores, y no abren secrets.json.
public sealed class UserSecretsSourceTests
{
    private const string SecretsFileName = "secrets.json";

    [Fact]
    public void PLAN0018_CA66_ApiCsproj_DeclaraUserSecretsIdIgualAlDelEnsamblado()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot.Path, "src", "Zentory.API", "Zentory.API.csproj"));
        var declared = csproj.Descendants("UserSecretsId").Select(e => e.Value.Trim()).ToList();

        Assert.Single(declared);
        Assert.True(Guid.TryParse(declared[0], out _), "UserSecretsId no es un GUID.");
        Assert.Equal(declared[0], ApiUserSecretsId());
    }

    [Fact]
    public async Task PLAN0018_CA66_DevelopmentHost_IncluyeLaFuenteDeUserSecrets()
    {
        await using var factory = InMemoryHost(Environments.Development);
        using var client = factory.CreateClient();

        Assert.True(HasUserSecretsSource(factory.Services),
            "El host Development no registró la fuente de user-secrets de Zentory.API.");
    }

    [Fact]
    public async Task PLAN0018_CA66_TestingHost_NoIncluyeLaFuenteDeUserSecrets()
    {
        await using var factory = new ZentoryApiFactory();
        using var client = factory.CreateClient();

        Assert.False(HasUserSecretsSource(factory.Services), "El host Testing registró la fuente de user-secrets.");
    }

    [Fact]
    public async Task PLAN0018_CA66_ProductionHost_NoIncluyeLaFuenteDeUserSecrets()
    {
        // Production no admite InMemory: PostgreSQL con Host=unused, que el arranque fuera de Development nunca abre.
        await using var factory = new MinimalHostFactory(Environments.Production, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]             = "false",
            [DatabaseStartupSettings.ConnectionStringKey] = "Host=unused;Database=zentory_unused",
            [HangfireSettings.ServerEnabledKey]           = "false",
        });
        using var client = factory.CreateClient();

        Assert.False(HasUserSecretsSource(factory.Services), "El host Production registró la fuente de user-secrets.");
    }

    private static MinimalHostFactory InMemoryHost(string environment)
        => new(environment, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-user-secrets-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
        });

    private static string ApiUserSecretsId()
    {
        var attribute = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>();
        Assert.NotNull(attribute);
        return attribute!.UserSecretsId;
    }

    // La fuente de user-secrets es un JSON llamado secrets.json servido desde la carpeta del UserSecretsId de la API.
    // Si esa carpeta no existe en la máquina, la fuente igual se registra (opcional), y el builder le asigna el proveedor
    // de archivos por defecto; basta entonces con que exista la fuente secrets.json.
    private static bool HasUserSecretsSource(IServiceProvider services)
    {
        var root = Assert.IsAssignableFrom<IConfigurationRoot>(services.GetRequiredService<IConfiguration>());
        var secretsDirectory = Path.GetDirectoryName(PathHelper.GetSecretsPathFromSecretsId(ApiUserSecretsId()))!;
        var secretsDirectoryExists = Directory.Exists(secretsDirectory);

        return root.Providers
            .OfType<JsonConfigurationProvider>()
            .Select(provider => provider.Source)
            .Any(source => string.Equals(source.Path, SecretsFileName, StringComparison.OrdinalIgnoreCase)
                        && (!secretsDirectoryExists
                            || source.FileProvider is PhysicalFileProvider physical
                               && SameDirectory(physical.Root, secretsDirectory)));
    }

    private static bool SameDirectory(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}

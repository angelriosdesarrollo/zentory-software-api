using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0018 (RF-4, RF-5, RF-7; D-2, D-4; SR-0018.2, SR-0018.4): arranque del host real (Program.cs) con PostgreSQL
// configurado y sin servidor de base. Ningún test conecta a una base: los hosts usan Host=unused, un host remoto
// ficticio que el arranque rechaza antes de conectar, o un socket Unix inexistente.
//
// Sin paralelismo: los hosts registran Hangfire, que fija JobStorage.Current global del proceso (R-6).
[Collection(NonParallelCollection.Name)]
public sealed class DatabaseStartupTests
{
    private const string UnusedConnectionString = "Host=unused;Database=zentory_unused;Username=zentory";

    // Valores trampa: si un mensaje del arranque filtrara configuración, aparecerían en él.
    private const string TrapPassword = "startup-trap-password";
    private const string TrapApiKey   = "startup-trap-resend-key";
    private const string RemoteHost   = "db.startup-trap.example.com";

    public static TheoryData<string> AllEnvironments => new()
    {
        Environments.Development,
        Environments.Production,
        AppEnvironments.Testing,
    };

    public static TheoryData<string, bool> HangfireSchemaByEnvironment => new()
    {
        { Environments.Development, true },
        { Environments.Production, false },
        { Environments.Staging, false },
        { AppEnvironments.Testing, false },
    };

    // ── CA-10: Production con Npgsql no migra ni conecta ────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA10_ProductionHost_ConNpgsqlHostUnused_ArrancaSinAbrirConexiones()
    {
        var recorder = new ConnectionOpenRecorder();
        await using var host = PostgresHost(Environments.Production, UnusedConnectionString);
        await using var factory = host.WithConnectionRecorder(recorder);

        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.True(db.Database.IsNpgsql());
        ConnectionRecorderWiring.AssertWired(db, recorder);
        Assert.Equal(0, recorder.Attempts);
    }

    [Fact]
    public async Task PLAN0018_CA10_DevelopmentHost_ConHostNoLocalSinOverride_FallaAntesDeConectarSinExponerValores()
    {
        var recorder = new ConnectionOpenRecorder();
        await using var host = PostgresHost(Environments.Development,
            $"Host={RemoteHost};Database=zentory;Username=zentory;Password={TrapPassword}");
        await using var factory = host.WithConnectionRecorder(recorder);

        var startupError = Record.Exception(() => factory.CreateClient());

        var error = FindInChain<InvalidOperationException>(startupError,
            e => e.Message.Contains(DatabaseStartupSettings.ConnectionStringKey, StringComparison.Ordinal));
        Assert.True(error is not null, $"El arranque no falló con la regla de host no local: {startupError?.GetType().Name}");
        Assert.Contains(DatabaseStartupSettings.AllowNonLocalDevelopmentKey, error!.Message);
        AssertHasNoConfigurationValues(error.Message);
        Assert.Equal(0, recorder.Attempts);
    }

    [Fact]
    public async Task PLAN0018_CA10_DevelopmentHost_ConPostgresInalcanzable_FallaConClaveYTipoSinValores()
    {
        // Socket Unix inexistente: cuenta como loopback, así que el arranque intenta migrar y el fallo de conexión es
        // inmediato y sin red (RF-5).
        var socketDirectory = $"/zentory-no-postgres-{Guid.NewGuid():N}";
        var recorder = new ConnectionOpenRecorder();
        await using var host = PostgresHost(Environments.Development,
            $"Host={socketDirectory};Database=zentory;Username=zentory;Password={TrapPassword};Timeout=2");
        await using var factory = host.WithConnectionRecorder(recorder);

        var startupError = Record.Exception(() => factory.CreateClient());

        var error = FindInChain<InvalidOperationException>(startupError,
            e => e.Message.Contains(DatabaseStartupSettings.ConnectionStringKey, StringComparison.Ordinal));
        Assert.True(error is not null, $"El arranque no falló con el error de conexión traducido: {startupError?.GetType().Name}");
        Assert.True(
            error!.Message.Contains("NpgsqlException", StringComparison.Ordinal)
            || error.Message.Contains("SocketException", StringComparison.Ordinal),
            "El mensaje no nombra el tipo del error de conexión.");
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(socketDirectory, error.Message);
        AssertHasNoConfigurationValues(error.Message);
        Assert.True(recorder.Attempts > 0, "El arranque no intentó migrar contra el host loopback.");
    }

    // ── CA-11: PostgreSQL sin cadena de conexión ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllEnvironments))]
    public async Task PLAN0018_CA11_Host_SinDefaultConnectionConUseInMemoryFalse_FallaNombrandoLaClaveSinValores(
        string environment)
    {
        // La cadena queda vacía por NeutralizedMachineSettings (la de la máquina nunca llega al host).
        await using var factory = new MinimalHostFactory(environment, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "false",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "false",
            ["Resend:ApiKey"]                   = TrapApiKey,
        });

        var startupError = Record.Exception(() => factory.CreateClient());

        var error = FindInChain<InvalidOperationException>(startupError,
            e => e.Message.Contains(DatabaseStartupSettings.ConnectionStringKey, StringComparison.Ordinal));
        Assert.True(error is not null, $"El arranque no falló con el fail-fast de la cadena: {startupError?.GetType().Name}");
        Assert.Contains(DatabaseSettings.UseInMemoryKey, error!.Message);
        Assert.Contains($"current environment: {environment}", error.Message);
        AssertHasNoConfigurationValues(error.Message);
    }

    // ── CA-65: esquema de Hangfire solo en Development ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PLAN0018_CA65_ProductionHost_PrepareSchemaIfNecessary_EsFalse()
    {
        var recorder = new ConnectionOpenRecorder();
        await using var host = PostgresHost(Environments.Production, UnusedConnectionString);
        await using var factory = host.WithConnectionRecorder(recorder);
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<PostgreSqlStorageOptions>();

        Assert.False(options.PrepareSchemaIfNecessary);
        Assert.Equal(0, recorder.Attempts);
    }

    [Theory]
    [MemberData(nameof(HangfireSchemaByEnvironment))]
    public async Task PLAN0018_CA65_AddInfrastructure_PrepareSchemaIfNecessary_SoloEnDevelopment(
        string environment, bool expected)
    {
        await using var provider = InfrastructureProvider(new HostingEnvironment { EnvironmentName = environment });

        Assert.Equal(expected, provider.GetRequiredService<PostgreSqlStorageOptions>().PrepareSchemaIfNecessary);
    }

    [Fact]
    public async Task PLAN0018_CA65_AddInfrastructure_SinAmbiente_NoPreparaElEsquema()
    {
        await using var provider = InfrastructureProvider(environment: null);

        Assert.False(provider.GetRequiredService<PostgreSqlStorageOptions>().PrepareSchemaIfNecessary);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Host real con PostgreSQL configurado y el servidor de Hangfire apagado.
    private static MinimalHostFactory PostgresHost(string environment, string connectionString)
        => new(environment, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]             = "false",
            [DatabaseStartupSettings.ConnectionStringKey] = connectionString,
            [HangfireSettings.ServerEnabledKey]           = "false",
            [DevSeedSettings.EnabledKey]                  = "false",
            ["Resend:ApiKey"]                             = TrapApiKey,
        });

    private static ServiceProvider InfrastructureProvider(IHostEnvironment? environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseSettings.UseInMemoryKey]             = "false",
                [DatabaseStartupSettings.ConnectionStringKey] = UnusedConnectionString,
                [HangfireSettings.ServerEnabledKey]           = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, environment);
        return services.BuildServiceProvider();
    }

    private static void AssertHasNoConfigurationValues(string message)
    {
        Assert.DoesNotContain(TrapPassword, message);
        Assert.DoesNotContain(TrapApiKey, message);
        Assert.DoesNotContain(RemoteHost, message);
        Assert.DoesNotContain("Host=", message);
        Assert.DoesNotContain("Password=", message);
        Assert.DoesNotContain(TestJwtSettings.Key, message);
        Assert.DoesNotContain(TestJwtSettings.CollaboratorKey, message);
    }

    private static TException? FindInChain<TException>(Exception? exception, Func<TException, bool> predicate)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException match && predicate(match)) return match;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    var found = FindInChain(inner, predicate);
                    if (found is not null) return found;
                }
            }
        }

        return null;
    }
}

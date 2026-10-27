using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0001 (RF-14; D-4, R-6): Hangfire:ServerEnabled apaga a la vez el servidor de jobs y el job recurrente, y por
// defecto (clave ausente) el servidor sí se registra.
//
// Sin paralelismo (R-6): RecurringJob y la configuración de Hangfire usan JobStorage.Current, global del proceso; un
// host Development que arrancara en paralelo podría programar su job en el storage que este test inspecciona.
[Collection(NonParallelCollection.Name)]
public sealed class HangfireFlagTests
{
    [Fact]
    public async Task PLAN0001_CA19_Hangfire_ServerDisabled_NoRegistraServidor()
    {
        await using var factory = new ZentoryApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal("false", factory.Services.GetRequiredService<IConfiguration>()[HangfireSettings.ServerEnabledKey]);

        var hostedServices = factory.Services.GetServices<IHostedService>().ToList();
        Assert.DoesNotContain(hostedServices, s => s is BackgroundJobServerHostedService);

        // El storage del contenedor, no JobStorage.Current (R-6).
        var jobStorage = factory.Services.GetRequiredService<JobStorage>();
        using var connection = jobStorage.GetConnection();
        Assert.DoesNotContain(connection.GetRecurringJobs(), job => job.Id == RecurringJobIds.MonthlyPilaAutoRequest);
    }

    [Fact]
    public async Task PLAN0001_CA19_Hangfire_ClaveAusente_RegistraServidor()
    {
        // Configuración sin Hangfire:ServerEnabled; InMemory para que resolver el storage no abra conexiones.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseSettings.UseInMemoryKey]  = "true",
                [DatabaseSettings.InMemoryNameKey] = $"zentory-hangfire-{Guid.NewGuid():N}",
            })
            .Build();
        Assert.Null(configuration[HangfireSettings.ServerEnabledKey]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);

        // Hangfire registra el servidor con una fábrica: se comprueba la instancia resuelta, sin arrancar el host.
        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.Contains(hostedServices, s => s is BackgroundJobServerHostedService);
    }

    [Fact]
    public async Task PLAN0001_CA33_Hangfire_ServerDisabledFueraDeTesting_LogWarning()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = new MinimalHostFactory("Development", new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]        = "true",
            [DatabaseSettings.InMemoryNameKey]       = $"zentory-hangfire-dev-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]      = "false",
            [DevSeedSettings.EnabledKey]             = "false",
            [TestJwtSettings.KeySetting]             = TestJwtSettings.Key,
            [TestJwtSettings.CollaboratorKeySetting] = TestJwtSettings.CollaboratorKey,
        });
        await using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));

        using var client = factory.CreateClient();

        Assert.Equal(Environments.Development, factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        var warnings = logs.Entries
            .Where(e => e.Level == LogLevel.Warning
                     && e.Message.Contains(HangfireSettings.ServerEnabledKey, StringComparison.Ordinal)
                     && e.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                     && e.Message.Contains(RecurringJobIds.MonthlyPilaAutoRequest, StringComparison.Ordinal))
            .ToList();
        Assert.True(warnings.Count == 1,
            $"Se esperaba un Warning del servidor de jobs apagado; logs Warning+: " +
            string.Join(" | ", logs.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message)));

        // Coherente con el log: tampoco hay servidor registrado en Development.
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s is BackgroundJobServerHostedService);
    }
}

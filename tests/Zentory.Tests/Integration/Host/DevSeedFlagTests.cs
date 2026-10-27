using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0001 (RF-15; R-6, R-7): DevDataSeeder solo corre en Development con DevSeed:Enabled. Cada test usa su propia
// base InMemory, y las lecturas usan IgnoreQueryFilters() para no depender de los filtros de tenant (H-08).
public sealed class DevSeedFlagTests
{
    [Fact]
    public async Task PLAN0001_CA20_DevSeed_Disabled_NoSiembraDatos()
    {
        await using var factory = new ZentoryApiFactory();
        using var client = factory.CreateClient();

        await AssertDatabaseIsEmptyAsync(factory.Services);
    }

    [Fact]
    public async Task PLAN0001_CA20_DevSeed_DisabledEnDevelopment_NoSiembraDatos()
    {
        // Refuerzo: en Testing el seeder nunca corre, así que el flag se comprueba también en Development, donde es
        // lo único que lo detiene.
        await using var factory = new MinimalHostFactory(Environments.Development, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]        = "true",
            [DatabaseSettings.InMemoryNameKey]       = $"zentory-devseed-off-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]      = "false",
            [DevSeedSettings.EnabledKey]             = "false",
            [TestJwtSettings.KeySetting]             = TestJwtSettings.Key,
            [TestJwtSettings.CollaboratorKeySetting] = TestJwtSettings.CollaboratorKey,
        });
        using var client = factory.CreateClient();

        Assert.Equal(Environments.Development, factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        await AssertDatabaseIsEmptyAsync(factory.Services);
    }

    [Fact]
    public async Task PLAN0001_CA33_DevSeed_EnabledEnTesting_NoSiembraDatos()
    {
        await using var baseFactory = new ZentoryApiFactory();
        await using var factory = baseFactory.WithWebHostBuilder(b => b
            .UseSetting(DevSeedSettings.EnabledKey, "true")
            .UseSetting(DatabaseSettings.InMemoryNameKey, $"zentory-devseed-on-{Guid.NewGuid():N}"));
        using var client = factory.CreateClient();

        Assert.Equal(AppEnvironments.Testing, factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.Equal("true", factory.Services.GetRequiredService<IConfiguration>()[DevSeedSettings.EnabledKey]);
        await AssertDatabaseIsEmptyAsync(factory.Services);
    }

    private static async Task AssertDatabaseIsEmptyAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.True(db.Database.IsInMemory());
        Assert.False(await db.Organizations.IgnoreQueryFilters().AnyAsync(), "La base tiene organizaciones sembradas.");
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(), "La base tiene usuarios sembrados.");
    }
}

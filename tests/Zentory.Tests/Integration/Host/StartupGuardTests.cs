using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Host;

// PLAN-0001 (RF-16, D-3; RF-14, RF-15, RF-24): el host real (Program.cs) arranca con InMemory solo en Development o
// Testing, y en Development sigue sembrando y sirviendo Swagger, el dashboard de Hangfire y el login.
public sealed class StartupGuardTests
{
    private const string SeededUserEmail    = "dev@zentorysoftware.com";
    private const string SeededUserPassword = "Zentory2026*";
    private const string SeededOrganization = "TechFactory CO";

    // Valores trampa: si el mensaje del fail-fast filtrara configuración, aparecerían en él.
    private const string SensitiveConnectionString =
        "Host=db.internal.example;Database=zentory;Username=zentory;Password=guard-test-password";

    public static TheoryData<string, string> DeployedEnvironments => new()
    {
        { "Production", "true" },
        { "Production", "True" },
        { "Staging",    "true" },
        { "Staging",    "True" },
    };

    [Theory]
    [MemberData(nameof(DeployedEnvironments))]
    public async Task PLAN0001_CA21_Host_WithInMemoryOutsideDevelopmentOrTesting_FailsFastWithoutLeakingConfiguration(
        string environment, string useInMemory)
    {
        await using var factory = new MinimalHostFactory(environment, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]        = useInMemory,
            ["ConnectionStrings:DefaultConnection"]  = SensitiveConnectionString,
            [TestJwtSettings.KeySetting]             = TestJwtSettings.Key,
            [TestJwtSettings.CollaboratorKeySetting] = TestJwtSettings.CollaboratorKey,
        });

        var startupError = Record.Exception(() => factory.CreateClient());

        var guardError = FindInChain<InvalidOperationException>(startupError,
            e => e.Message.Contains(DatabaseSettings.UseInMemoryKey, StringComparison.Ordinal));
        Assert.True(guardError is not null, $"El arranque no falló con el fail-fast de InMemory: {startupError}");
        Assert.Contains($"current environment: {environment}", guardError!.Message);
        Assert.DoesNotContain("Host=", guardError.Message);
        Assert.DoesNotContain("Password", guardError.Message);
        Assert.DoesNotContain("guard-test-password", guardError.Message);
        Assert.DoesNotContain(TestJwtSettings.Key, guardError.Message);
        Assert.DoesNotContain(TestJwtSettings.CollaboratorKey, guardError.Message);
    }

    [Theory]
    [InlineData(AppEnvironments.Testing, "true")]
    [InlineData(AppEnvironments.Testing, "True")]
    [InlineData("Development",           "true")]
    [InlineData("Development",           "True")]
    public async Task PLAN0001_CA21_Host_WithInMemoryInDevelopmentOrTesting_StartsOnInMemoryDatabase(
        string environment, string useInMemory)
    {
        await using var factory = new MinimalHostFactory(environment, new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]        = useInMemory,
            [DatabaseSettings.InMemoryNameKey]       = $"zentory-guard-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey]      = "false",
            [DevSeedSettings.EnabledKey]             = "false",
            [TestJwtSettings.KeySetting]             = TestJwtSettings.Key,
            [TestJwtSettings.CollaboratorKeySetting] = TestJwtSettings.CollaboratorKey,
        });

        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        Assert.True(db.Database.IsInMemory());
    }

    [Fact]
    public async Task PLAN0001_CA23_DevelopmentHost_WithInMemory_SeedsServesSwaggerAndHangfireAndLogsInSeededUser()
    {
        // Configuración Development mínima y en memoria (InMemory + claves JWT locales de prueba, sin Anthropic, Resend
        // ni R2, así el almacenamiento cae a disco). Hangfire:ServerEnabled y DevSeed:Enabled quedan sin declarar para
        // ejercitar los valores por defecto, como en un dotnet run local.
        await using var factory = new MinimalHostFactory("Development", new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]        = "true",
            [DatabaseSettings.InMemoryNameKey]       = $"zentory-dev-{Guid.NewGuid():N}",
            [TestJwtSettings.KeySetting]             = TestJwtSettings.Key,
            [TestJwtSettings.CollaboratorKeySetting] = TestJwtSettings.CollaboratorKey,
        });
        using var client = factory.CreateClient();

        // Seed de desarrollo
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
            Assert.True(db.Database.IsInMemory());
            Assert.True(await db.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Name == SeededOrganization));
            Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == SeededUserEmail));
        }

        // Swagger
        using var swaggerUi = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, swaggerUi.StatusCode);
        using var swaggerDoc = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerDoc.StatusCode);

        // Dashboard de Hangfire: su filtro por defecto solo deja pasar peticiones locales, como en dotnet run.
        var dashboard = await factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/hangfire";
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Connection.LocalIpAddress = IPAddress.Loopback;
        });
        Assert.Equal(StatusCodes.Status200OK, dashboard.Response.StatusCode);

        // Login con un usuario sembrado
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = SeededUserEmail, password = SeededUserPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("accessToken").GetString()));
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

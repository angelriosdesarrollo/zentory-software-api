using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zentory.API.Extensions;
using Zentory.Infrastructure.Extensions;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Seeding;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Seeding;

// PLAN-0018 (RF-57, D-37; SR-0018.44; parte de CA-71, que se cierra en T4b con los escenarios demo): la contraseña de
// las personas demo. Los valores de prueba se generan en cada corrida; las aserciones comparan sin imprimirlos.
public sealed class DemoPasswordResolverTests
{
    private const string RemoteHost = "db.example.com";

    public static TheoryData<string, bool, bool> LocalTargets => new()
    {
        { Environments.Development, true,  false },  // InMemory
        { Environments.Development, false, true  },  // PostgreSQL en loopback
    };

    // Ambiente distinto de Development (con cualquier base) o Development contra un host no local.
    public static TheoryData<string, bool, bool> NonLocalTargets => new()
    {
        { Environments.Production,  true,  false },
        { Environments.Production,  false, true  },
        { Environments.Staging,     false, true  },
        { AppEnvironments.Testing,  true,  false },
        { Environments.Development, false, false },
    };

    public static TheoryData<string, bool, bool> AllTargets
    {
        get
        {
            var all = new TheoryData<string, bool, bool>();
            foreach (var row in LocalTargets.Concat(NonLocalTargets))
                all.Add((string)row[0], (bool)row[1], (bool)row[2]);
            return all;
        }
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void PLAN0018_CA71_DemoPasswordResolver_ConDemoDataPasswordDefinida_DevuelveEseValor(
        string environment, bool isInMemory, bool isLoopbackHost)
    {
        var password = NewValidPassword();

        var resolved = DemoPasswordResolver.Resolve(
            Configuration(password), new DemoSeedTarget(environment, isInMemory, isLoopbackHost), NewLogger().Logger);

        Assert.True(resolved == password, "No se devolvió el valor de DemoData:Password.");
    }

    [Theory]
    [MemberData(nameof(LocalTargets))]
    public void PLAN0018_CA71_DemoPasswordResolver_EnLocalSinLaVariable_DevuelveElValorDocumentado(
        string environment, bool isInMemory, bool isLoopbackHost)
    {
        var target = new DemoSeedTarget(environment, isInMemory, isLoopbackHost);

        Assert.True(target.IsLocal);
        Assert.True(DemoPasswordResolver.Resolve(Configuration(null), target, NewLogger().Logger)
                    == DemoPasswordResolver.LocalDefaultPassword);
        // Vacía cuenta como ausente: es lo que deja MinimalHostFactory al neutralizar la clave.
        Assert.True(DemoPasswordResolver.Resolve(Configuration(string.Empty), target, NewLogger().Logger)
                    == DemoPasswordResolver.LocalDefaultPassword);
    }

    [Theory]
    [MemberData(nameof(NonLocalTargets))]
    public void PLAN0018_CA71_DemoPasswordResolver_FueraDeLocalSinLaVariable_FallaNombrandoLaClave(
        string environment, bool isInMemory, bool isLoopbackHost)
    {
        var target = new DemoSeedTarget(environment, isInMemory, isLoopbackHost);

        var error = Assert.Throws<InvalidOperationException>(
            () => DemoPasswordResolver.Resolve(Configuration(null), target, NewLogger().Logger));

        Assert.False(target.IsLocal);
        Assert.Contains(DemoDataSettings.PasswordKey, error.Message);
        AssertHasNoPassword(error.Message, DemoPasswordResolver.LocalDefaultPassword);
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void PLAN0018_CA71_DemoPasswordResolver_ConValorDeSieteCaracteres_FallaNombrandoLaClaveSinElValor(
        string environment, bool isInMemory, bool isLoopbackHost)
    {
        var tooShort = NewValidPassword()[..7];

        var error = Assert.Throws<InvalidOperationException>(() => DemoPasswordResolver.Resolve(
            Configuration(tooShort), new DemoSeedTarget(environment, isInMemory, isLoopbackHost), NewLogger().Logger));

        Assert.Contains(DemoDataSettings.PasswordKey, error.Message);
        AssertHasNoPassword(error.Message, tooShort);
    }

    [Theory]
    [MemberData(nameof(NonLocalTargets))]
    public void PLAN0018_CA71_DemoPasswordResolver_FueraDeLocalConElValorPorDefecto_FallaSinElValor(
        string environment, bool isInMemory, bool isLoopbackHost)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DemoPasswordResolver.Resolve(
            Configuration(DemoPasswordResolver.LocalDefaultPassword),
            new DemoSeedTarget(environment, isInMemory, isLoopbackHost), NewLogger().Logger));

        Assert.Contains(DemoDataSettings.PasswordKey, error.Message);
        AssertHasNoPassword(error.Message, DemoPasswordResolver.LocalDefaultPassword);
    }

    [Theory]
    [MemberData(nameof(LocalTargets))]
    public void PLAN0018_CA71_DemoPasswordResolver_EnLocalConElValorPorDefecto_LoAcepta(
        string environment, bool isInMemory, bool isLoopbackHost)
    {
        var resolved = DemoPasswordResolver.Resolve(
            Configuration(DemoPasswordResolver.LocalDefaultPassword),
            new DemoSeedTarget(environment, isInMemory, isLoopbackHost), NewLogger().Logger);

        Assert.True(resolved == DemoPasswordResolver.LocalDefaultPassword);
    }

    [Fact]
    public void PLAN0018_CA71_DemoPasswordResolver_ValorPorDefecto_CumpleLaPoliticaDeContrasenas()
    {
        Assert.True(Zentory.Application.Common.Security.PasswordPolicy.IsSatisfiedBy(DemoPasswordResolver.LocalDefaultPassword));
    }

    [Fact]
    public void PLAN0018_CA71_DemoPasswordResolver_EnTodosLosCaminos_NoRegistraLaContrasena()
    {
        var configured = NewValidPassword();
        var (provider, logger) = NewLogger();
        var local    = new DemoSeedTarget(Environments.Development, IsInMemory: true, IsLoopbackHost: false);
        var nonLocal = new DemoSeedTarget(Environments.Production, IsInMemory: false, IsLoopbackHost: false);

        DemoPasswordResolver.Resolve(Configuration(configured), local, logger);
        DemoPasswordResolver.Resolve(Configuration(configured), nonLocal, logger);
        DemoPasswordResolver.Resolve(Configuration(null), local, logger);
        Assert.ThrowsAny<InvalidOperationException>(() => DemoPasswordResolver.Resolve(Configuration(null), nonLocal, logger));
        Assert.ThrowsAny<InvalidOperationException>(
            () => DemoPasswordResolver.Resolve(Configuration(configured[..7]), local, logger));
        Assert.ThrowsAny<InvalidOperationException>(
            () => DemoPasswordResolver.Resolve(Configuration(DemoPasswordResolver.LocalDefaultPassword), nonLocal, logger));

        // Control: el camino del valor por defecto sí escribe un log, así que el proveedor está capturando.
        Assert.NotEmpty(provider.Entries);
        foreach (var entry in provider.Entries)
        {
            AssertHasNoPassword(entry.Message, configured);
            AssertHasNoPassword(entry.Message, configured[..7]);
            AssertHasNoPassword(entry.Message, DemoPasswordResolver.LocalDefaultPassword);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PLAN0018_CA71_DevelopmentInMemoryConDemo_LasPersonasDemoUsanLaContrasenaResueltaSinRegistrarla(
        bool withConfiguredPassword)
    {
        var configured = withConfiguredPassword ? NewValidPassword() : null;
        var expected = configured ?? DemoPasswordResolver.LocalDefaultPassword;
        var logs = new CapturingLoggerProvider();
        var settings = new Dictionary<string, string?>
        {
            [DatabaseSettings.UseInMemoryKey]   = "true",
            [DatabaseSettings.InMemoryNameKey]  = $"zentory-demo-password-{Guid.NewGuid():N}",
            [HangfireSettings.ServerEnabledKey] = "false",
            [DevSeedSettings.EnabledKey]        = "true",
        };
        if (configured is not null) settings[DemoDataSettings.PasswordKey] = configured;

        await using var baseFactory = new MinimalHostFactory(Environments.Development, settings);
        await using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(logs)));
        using var client = factory.CreateClient();

        Assert.Equal(Environments.Development, factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var hashes = await db.Users.IgnoreQueryFilters()
            .Where(u => u.PasswordHash != null).Select(u => u.PasswordHash!).ToListAsync();

        Assert.NotEmpty(hashes);
        Assert.All(hashes, hash => Assert.True(BCrypt.Net.BCrypt.Verify(expected, hash),
            "Una persona demo no tiene la contraseña resuelta."));
        foreach (var entry in logs.Entries)
            AssertHasNoPassword(entry.Message, expected);
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    // Contraseña válida y distinta en cada corrida (≥ 8 caracteres, ASCII): nunca es un literal del archivo.
    private static string NewValidPassword() => $"Demo-{Guid.NewGuid():N}"[..20];

    private static IConfiguration Configuration(string? password)
    {
        var values = new Dictionary<string, string?>();
        if (password is not null) values[DemoDataSettings.PasswordKey] = password;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static (CapturingLoggerProvider Provider, ILogger Logger) NewLogger()
    {
        var provider = new CapturingLoggerProvider();
        return (provider, provider.CreateLogger(nameof(DemoPasswordResolverTests)));
    }

    // Sin Assert.DoesNotContain: su mensaje de fallo imprimiría el texto completo con la contraseña.
    private static void AssertHasNoPassword(string text, string password)
        => Assert.False(text.Contains(password, StringComparison.Ordinal), "El texto contiene la contraseña.");
}

using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Zentory.API.Extensions;

namespace Zentory.Tests.Unit.Host;

// PLAN-0018 (RF-4, RF-5; D-2; SR-0018.2, SR-0018.33, SR-0018.44): regla pura del arranque de base. Cubre todos los
// casos de CA-10; el arranque real con host Production está en Integration/Host/DatabaseStartupTests.
public sealed class DatabaseStartupPolicyTests
{
    private const string RemoteHost = "db.example.com";

    public static TheoryData<string> LoopbackHosts => new()
    {
        "localhost",
        "LOCALHOST",
        "127.0.0.1",
        "::1",
        "/var/run/postgresql",
        "/tmp",
        "localhost:5433",
        "[::1]:5432",
        "localhost,127.0.0.1",
    };

    public static TheoryData<string?> NonLoopbackHosts => new()
    {
        RemoteHost,
        "10.0.0.5",
        "127.0.0.2",
        "localhost.example.com",
        "localhost,db.example.com",
        "",
        null,
    };

    public static TheoryData<string> NonDevelopmentEnvironments => new()
    {
        AppEnvironments.Testing,
        Environments.Production,
        Environments.Staging,
        "prod",
    };

    // ── Development + Npgsql + loopback → Migrate ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LoopbackHosts))]
    public void PLAN0018_CA10_Decide_DevelopmentNpgsqlLoopback_MigraSiembraReferenciaYDemo(string host)
    {
        var decision = Decide(Environments.Development, isRelational: true, host, allowNonLocal: false, devSeedEnabled: true);

        Assert.True(decision.Migrate);
        Assert.True(decision.SeedReference);
        Assert.True(decision.SeedDemo);
        Assert.Null(decision.Fail);
    }

    [Fact]
    public void PLAN0018_CA10_Decide_DevelopmentNpgsqlLoopbackConDevSeedApagado_MigraSinDemo()
    {
        var decision = Decide(Environments.Development, isRelational: true, "localhost", allowNonLocal: false, devSeedEnabled: false);

        Assert.True(decision.Migrate);
        Assert.True(decision.SeedReference);
        Assert.False(decision.SeedDemo);
        Assert.Null(decision.Fail);
    }

    // ── InMemory u otro ambiente → no migra ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PLAN0018_CA10_Decide_DevelopmentInMemory_NoMigra(bool devSeedEnabled)
    {
        var decision = Decide(Environments.Development, isRelational: false, host: null, allowNonLocal: false, devSeedEnabled);

        Assert.False(decision.Migrate);
        Assert.False(decision.WriteLocalMarker);
        Assert.True(decision.SeedReference);
        Assert.Equal(devSeedEnabled, decision.SeedDemo);
        Assert.Null(decision.Fail);
    }

    [Theory]
    [MemberData(nameof(NonDevelopmentEnvironments))]
    public void PLAN0018_CA10_Decide_AmbienteDistintoDeDevelopment_NoMigraNiSiembra(string environment)
    {
        foreach (var isRelational in new[] { true, false })
        foreach (var allowNonLocal in new[] { true, false })
        foreach (var host in new[] { "localhost", RemoteHost })
        {
            var decision = Decide(environment, isRelational, host, allowNonLocal, devSeedEnabled: true, hasAppliedMigrations: false);

            Assert.Equal(DatabaseStartupDecision.Nothing, decision);
        }
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("db")]
    public void PLAN0018_CA10_Decide_ConVerboDeCli_NoHaceNada(string cliVerb)
    {
        var decision = DatabaseStartupPolicy.Decide(
            Environments.Development, isRelational: true, "localhost", allowNonLocal: false, devSeedEnabled: true, cliVerb,
            hasAppliedMigrations: false);

        Assert.Equal(DatabaseStartupDecision.Nothing, decision);
        Assert.True(DatabaseStartupPolicy.IsCliVerb(cliVerb));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("--urls")]
    [InlineData("seeds")]
    public void PLAN0018_CA10_IsCliVerb_ArgumentoQueNoEsVerbo_EsFalse(string? argument)
    {
        Assert.False(DatabaseStartupPolicy.IsCliVerb(argument));
    }

    // ── Development + Npgsql + host no local ─────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NonLoopbackHosts))]
    public void PLAN0018_CA10_Decide_DevelopmentHostNoLocalSinOverride_FallaSinMigrarNiSembrarDemo(string? host)
    {
        var decision = Decide(Environments.Development, isRelational: true, host, allowNonLocal: false, devSeedEnabled: true);

        Assert.False(decision.Migrate);
        Assert.False(decision.SeedReference);
        Assert.False(decision.SeedDemo);
        Assert.False(decision.WriteLocalMarker);
        Assert.NotNull(decision.Fail);
        Assert.Contains(DatabaseStartupSettings.ConnectionStringKey, decision.Fail);
        Assert.Contains(DatabaseStartupSettings.AllowNonLocalDevelopmentKey, decision.Fail);
        if (!string.IsNullOrEmpty(host))
            Assert.DoesNotContain(host, decision.Fail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RemoteHost)]
    [InlineData("10.0.0.5")]
    public void PLAN0018_CA10_Decide_DevelopmentHostNoLocalConOverrideYDevSeed_MigraYReferenciaSinDemo(string host)
    {
        var decision = Decide(Environments.Development, isRelational: true, host, allowNonLocal: true, devSeedEnabled: true,
            hasAppliedMigrations: false);

        Assert.True(decision.Migrate);
        Assert.True(decision.SeedReference);
        Assert.False(decision.SeedDemo);
        Assert.False(decision.WriteLocalMarker);
        Assert.Null(decision.Fail);
    }

    // ── Marca de base local (SR-0018.33) ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LoopbackHosts))]
    public void PLAN0018_CA10_Decide_MigrateSobreBaseSinMigracionesLoopbackSinOverride_EscribeMarcaLocal(string host)
    {
        var decision = Decide(Environments.Development, isRelational: true, host, allowNonLocal: false, devSeedEnabled: true,
            hasAppliedMigrations: false);

        Assert.True(decision.Migrate);
        Assert.True(decision.WriteLocalMarker);
    }

    [Fact]
    public void PLAN0018_CA10_Decide_MigrateSobreBaseConMigraciones_NoEscribeMarcaLocal()
    {
        var decision = Decide(Environments.Development, isRelational: true, "localhost", allowNonLocal: false, devSeedEnabled: true,
            hasAppliedMigrations: true);

        Assert.True(decision.Migrate);
        Assert.False(decision.WriteLocalMarker);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData(RemoteHost)]
    public void PLAN0018_CA10_Decide_MigrateSobreBaseSinMigracionesConOverride_NoEscribeMarcaLocal(string host)
    {
        var decision = Decide(Environments.Development, isRelational: true, host, allowNonLocal: true, devSeedEnabled: true,
            hasAppliedMigrations: false);

        Assert.True(decision.Migrate);
        Assert.False(decision.WriteLocalMarker);
    }

    // ── Host desde la cadena ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LoopbackHosts))]
    public void PLAN0018_CA10_IsLoopbackHost_HostLocal_EsTrue(string host)
    {
        Assert.True(DatabaseStartupPolicy.IsLoopbackHost(host));
    }

    [Theory]
    [MemberData(nameof(NonLoopbackHosts))]
    public void PLAN0018_CA10_IsLoopbackHost_HostNoLocal_EsFalse(string? host)
    {
        Assert.False(DatabaseStartupPolicy.IsLoopbackHost(host));
    }

    [Theory]
    [InlineData("Host=localhost;Database=zentory", "localhost")]
    [InlineData("Host=/var/run/postgresql;Database=zentory", "/var/run/postgresql")]
    [InlineData("Server=db.example.com;Database=zentory", RemoteHost)]
    public void PLAN0018_CA10_HostOf_CadenaNpgsql_DevuelveElHostDelBuilder(string connectionString, string expected)
    {
        Assert.Equal(expected, DatabaseStartupPolicy.HostOf(connectionString));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotAKeyword=value")]
    public void PLAN0018_CA10_HostOf_CadenaVaciaOIlegible_EsNullYCuentaComoNoLocal(string? connectionString)
    {
        var host = DatabaseStartupPolicy.HostOf(connectionString);

        Assert.Null(host);
        Assert.False(DatabaseStartupPolicy.IsLoopbackHost(host));
    }

    // ── Fallo de conexión al migrar (RF-5) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void PLAN0018_CA10_ConnectionFailure_NpgsqlException_NombraClaveYTipoSinMensajeNiInner()
    {
        const string leakyMessage = "Failed to connect to 10.1.2.3:5432 Password=connection-failure-trap";
        var original = new NpgsqlException(leakyMessage, new SocketException((int)SocketError.ConnectionRefused));

        var translated = DatabaseStartupPolicy.ConnectionFailure(original);

        Assert.NotNull(translated);
        Assert.Contains(DatabaseStartupSettings.ConnectionStringKey, translated!.Message);
        Assert.Contains(nameof(NpgsqlException), translated.Message);
        Assert.DoesNotContain("10.1.2.3", translated.Message);
        Assert.DoesNotContain("connection-failure-trap", translated.Message);
        Assert.Null(translated.InnerException);
    }

    [Fact]
    public void PLAN0018_CA10_ConnectionFailure_SocketExceptionEnvuelta_NombraClaveYTipo()
    {
        var original = new InvalidOperationException("wrapper", new SocketException((int)SocketError.HostNotFound));

        var translated = DatabaseStartupPolicy.ConnectionFailure(original);

        Assert.NotNull(translated);
        Assert.Contains(DatabaseStartupSettings.ConnectionStringKey, translated!.Message);
        Assert.Contains(nameof(SocketException), translated.Message);
        Assert.Null(translated.InnerException);
    }

    [Fact]
    public void PLAN0018_CA10_ConnectionFailure_ExcepcionQueNoEsDeConexion_EsNull()
    {
        Assert.Null(DatabaseStartupPolicy.ConnectionFailure(new InvalidOperationException("model error")));
    }

    private static DatabaseStartupDecision Decide(
        string environment, bool isRelational, string? host, bool allowNonLocal, bool devSeedEnabled,
        bool hasAppliedMigrations = true)
        => DatabaseStartupPolicy.Decide(
            environment, isRelational, host, allowNonLocal, devSeedEnabled, cliVerb: null, hasAppliedMigrations);
}

using Zentory.API.Cli;
using Zentory.Infrastructure.Persistence.Seeding;

namespace Zentory.Tests.Unit.Cli;

// PLAN-0018 CA-86 y CA-124 (RF-63 b–h; ADR-0007 §4b; D-34; SR-0018.32, SR-0018.33): la regla de db init como función pura.
// DecidePreConnect decide antes de abrir conexión; DecideConnected, con lo que se leyó de la base y antes de escribir.
public sealed class DbInitPolicyTests
{
    private const string Development = DbInitPolicy.RequiredEnvironment;
    private const string Loopback    = "localhost";
    private const string Remote      = "db-remote.invalid";
    private const string Database    = "zentory_local";

    // ── CA-86: lista blanca de ambiente ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("prod")]
    [InlineData("Testing")]
    [InlineData("development")]
    [InlineData(" Development")]
    [InlineData("")]
    [InlineData(null)]
    public void PLAN0018_CA86_DecidePreConnect_AmbienteDistintoDeDevelopmentExacto_FallaConCodigoDistintoDeCero(string? environment)
    {
        var decision = PreConnect(environment: environment);

        Assert.False(decision.Proceed);
        Assert.NotEqual(CliExitCodes.Success, decision.ExitCode);
        Assert.Contains("ASPNETCORE_ENVIRONMENT", decision.Fail);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("/var/run/postgresql")]
    public void PLAN0018_CA124_DecidePreConnect_DevelopmentConHostLoopbackSinOverride_PermiteYEscribeMarca(string host)
    {
        var decision = PreConnect(host: host);

        Assert.True(decision.Proceed);
        Assert.Equal(CliExitCodes.Success, decision.ExitCode);
        Assert.True(decision.WriteLocalMarker);
    }

    // ── CA-86: host no local ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Remote)]
    [InlineData("10.0.0.5")]
    [InlineData("localhost,db-remote.invalid")]
    [InlineData(null)]
    public void PLAN0018_CA86_DecidePreConnect_HostNoLocalSinFlag_Falla(string? host)
    {
        var decision = PreConnect(host: host);

        Assert.False(decision.Proceed);
        Assert.NotEqual(CliExitCodes.Success, decision.ExitCode);
        Assert.Contains(DbInitPolicy.AllowNonLocalFlag, decision.Fail);
    }

    [Fact]
    public void PLAN0018_CA124_DecidePreConnect_HostNoLocalConFlag_PermiteSinMarcaLocal()
    {
        var decision = PreConnect(host: Remote, allowNonLocalFlag: true);

        Assert.True(decision.Proceed);
        Assert.False(decision.WriteLocalMarker);
    }

    [Fact]
    public void PLAN0018_CA124_DecidePreConnect_HostLoopbackConFlag_PermiteSinMarcaLocal()
    {
        // Con el override no hay marca: un túnel a localhost no deja una base remota marcada como local (SR-0018.33).
        var decision = PreConnect(host: Loopback, allowNonLocalFlag: true);

        Assert.True(decision.Proceed);
        Assert.False(decision.WriteLocalMarker);
    }

    [Fact]
    public void PLAN0018_CA86_DecidePreConnect_NoTieneParametroDeConfiguracionParaElOverride()
    {
        // La configuración (Database:AllowNonLocalDevelopment) no entra en la regla: solo el flag de línea de comandos.
        var parameters = typeof(DbInitPolicy).GetMethod(nameof(DbInitPolicy.DecidePreConnect))!
            .GetParameters().Select(p => p.Name).ToList();

        Assert.Equal(
            ["environment", "host", "allowNonLocalFlag", "reset", "demo", "demoPasswordStatus", "interactive",
             "confirmDatabaseArg"],
            parameters);
    }

    // ── CA-86: --reset solo loopback y sin override ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Remote, true)]
    [InlineData(Remote, false)]
    [InlineData(Loopback, true)]
    public void PLAN0018_CA86_DecidePreConnect_ResetConHostRemotoOConOverride_Falla(string host, bool allowNonLocalFlag)
    {
        var decision = PreConnect(host: host, allowNonLocalFlag: allowNonLocalFlag, reset: true, confirmDatabaseArg: Database);

        Assert.False(decision.Proceed);
        Assert.NotEqual(CliExitCodes.Success, decision.ExitCode);
        Assert.Contains(DbInitPolicy.ResetFlag, decision.Fail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PLAN0018_CA86_DecidePreConnect_ResetSinTerminalSinConfirmDatabase_FallaSinConfirmar(string? confirm)
    {
        // --yes no es parámetro de la regla: no reemplaza a --confirm-database (SR-0018.32 e).
        var decision = PreConnect(reset: true, interactive: false, confirmDatabaseArg: confirm);

        Assert.False(decision.Proceed);
        Assert.Equal(CliExitCodes.NotConfirmed, decision.ExitCode);
        Assert.Contains(DbInitPolicy.ConfirmDatabaseFlag, decision.Fail);
    }

    [Theory]
    [InlineData(false, Database)]
    [InlineData(true, null)]
    [InlineData(true, Database)]
    public void PLAN0018_CA124_DecidePreConnect_ResetLoopbackConTerminalOConfirmDatabase_Permite(bool interactive, string? confirm)
    {
        var decision = PreConnect(reset: true, interactive: interactive, confirmDatabaseArg: confirm);

        Assert.True(decision.Proceed);
        Assert.True(decision.WriteLocalMarker);
    }

    // ── CA-86: contraseña demo fuera de local ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DemoPasswordStatus.NotConfigured)]
    [InlineData(DemoPasswordStatus.LocalDefault)]
    public void PLAN0018_CA86_DecidePreConnect_DemoFueraDeLocalSinContrasenaOConLaPorDefecto_Falla(DemoPasswordStatus status)
    {
        var decision = PreConnect(host: Remote, allowNonLocalFlag: true, demo: true, demoPasswordStatus: status);

        Assert.False(decision.Proceed);
        Assert.NotEqual(CliExitCodes.Success, decision.ExitCode);
        Assert.Contains(DemoDataSettings.PasswordKey, decision.Fail);
    }

    [Fact]
    public void PLAN0018_CA86_DecidePreConnect_DemoFueraDeLocalConContrasenaPropia_Permite()
    {
        var decision = PreConnect(host: Remote, allowNonLocalFlag: true, demo: true,
            demoPasswordStatus: DemoPasswordStatus.Configured);

        Assert.True(decision.Proceed);
    }

    [Theory]
    [InlineData(DemoPasswordStatus.NotConfigured)]
    [InlineData(DemoPasswordStatus.LocalDefault)]
    public void PLAN0018_CA86_DecidePreConnect_DemoEnLocal_PermiteElValorPorDefecto(DemoPasswordStatus status)
    {
        Assert.True(PreConnect(demo: true, demoPasswordStatus: status).Proceed);
    }

    [Fact]
    public void PLAN0018_CA86_DemoPasswordStatusOf_ClasificaSinExponerElValor()
    {
        Assert.Equal(DemoPasswordStatus.NotConfigured, DbInitPolicy.DemoPasswordStatusOf(null));
        Assert.Equal(DemoPasswordStatus.NotConfigured, DbInitPolicy.DemoPasswordStatusOf(string.Empty));
        Assert.Equal(DemoPasswordStatus.LocalDefault,
            DbInitPolicy.DemoPasswordStatusOf(DemoPasswordResolver.LocalDefaultPassword));
        Assert.Equal(DemoPasswordStatus.Configured,
            DbInitPolicy.DemoPasswordStatusOf(DemoPasswordResolver.LocalDefaultPassword + "x"));
    }

    // ── CA-86 y CA-124: con la base ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PLAN0018_CA86_DecideConnected_BaseConUsuariosUOrganizacionesSinReset_Falla(bool hasLocalMarker)
    {
        var decision = DbInitPolicy.DecideConnected(true, hasLocalMarker, reset: false, null, Database);

        Assert.False(decision.Proceed);
        Assert.Equal(CliExitCodes.Error, decision.ExitCode);
        Assert.Contains(DbInitPolicy.ResetFlag, decision.Fail);
    }

    [Fact]
    public void PLAN0018_CA87_DecideConnected_BaseVaciaSinReset_Permite()
    {
        Assert.True(DbInitPolicy.DecideConnected(false, false, reset: false, null, Database).Proceed);
    }

    [Fact]
    public void PLAN0018_CA124_DecideConnected_ResetSobreBaseConDatosSinMarca_FallaAunqueElNombreCoincida()
    {
        var decision = DbInitPolicy.DecideConnected(true, hasLocalMarker: false, reset: true, Database, Database);

        Assert.False(decision.Proceed);
        Assert.Equal(CliExitCodes.Error, decision.ExitCode);
    }

    [Fact]
    public void PLAN0018_CA124_DecideConnected_ResetSobreBaseConDatosYMarcaConNombreCorrecto_Permite()
    {
        Assert.True(DbInitPolicy.DecideConnected(true, hasLocalMarker: true, reset: true, $" {Database} ", Database).Proceed);
    }

    [Fact]
    public void PLAN0018_CA124_DecideConnected_ResetSobreBaseVaciaSinMarcaConNombreCorrecto_Permite()
    {
        Assert.True(DbInitPolicy.DecideConnected(false, hasLocalMarker: false, reset: true, Database, Database).Proceed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zentory")]
    [InlineData("ZENTORY_LOCAL")]
    [InlineData("y")]
    public void PLAN0018_CA86_DecideConnected_ResetConNombreAusenteODistinto_FallaSinConfirmar(string? typed)
    {
        var decision = DbInitPolicy.DecideConnected(true, hasLocalMarker: true, reset: true, typed, Database);

        Assert.False(decision.Proceed);
        Assert.Equal(CliExitCodes.NotConfirmed, decision.ExitCode);
        Assert.DoesNotContain(Database, decision.Fail);
    }

    [Fact]
    public void PLAN0018_CA86_DecideConnected_ResetConCadenaSinDatabase_FallaSinConfirmar()
    {
        Assert.Equal(CliExitCodes.NotConfirmed,
            DbInitPolicy.DecideConnected(false, false, reset: true, string.Empty, null).ExitCode);
    }

    private static DbInitDecision PreConnect(
        string? environment = Development,
        string? host = Loopback,
        bool allowNonLocalFlag = false,
        bool reset = false,
        bool demo = false,
        DemoPasswordStatus demoPasswordStatus = DemoPasswordStatus.NotConfigured,
        bool interactive = false,
        string? confirmDatabaseArg = null)
        => DbInitPolicy.DecidePreConnect(environment, host, allowNonLocalFlag, reset, demo, demoPasswordStatus, interactive,
            confirmDatabaseArg);
}

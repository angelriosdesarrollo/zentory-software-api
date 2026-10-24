using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Schema;

// PLAN-0001 (RF-19; ADR-0001 §Decisión 5): el modelo de EF se traduce a DDL de PostgreSQL sin base de datos. No crea
// migraciones ni toca un servidor: GenerateCreateScript() solo recorre el modelo, y un interceptor deja constancia de
// cualquier intento de abrir conexión.
public sealed class PostgresModelTests
{
    [Fact]
    public void PLAN0001_CA18_PostgresModel_GenerateCreateScript_SinConexion_ContieneCreateTable()
    {
        var connections = new ConnectionOpenRecorder();
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql("Host=unused")
            .AddInterceptors(connections)
            .Options;
        using var db = new ZentoryDbContext(options);

        var script = db.Database.GenerateCreateScript();

        Assert.Equal(0, connections.Attempts);
        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("CREATE TABLE", script, StringComparison.Ordinal);

        // Cada tabla del modelo tiene su DDL: un mapeo que Npgsql no sepa traducir rompe aquí y no en el primer
        // despliegue contra PostgreSQL.
        var tables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .OfType<string>()
            .Distinct()
            .ToList();
        Assert.NotEmpty(tables);
        Assert.All(tables, table => Assert.Contains(table, script, StringComparison.Ordinal));
    }

    // TEMPORAL — válido solo hasta PLAN-0018. La épica PLAN-0008 ya decidió que PLAN-0018 crea la migración inicial;
    // ese plan debe borrar este test y reemplazarlo por uno de "sin cambios de modelo pendientes" (el modelo coincide
    // con la última migración). Mientras tanto protege la regla de rules.backend: sin migraciones EF ni ALTER TABLE.
    [Fact]
    public void PLAN0001_CA24_HastaPlan0018_SinArchivosDeMigracion_NiAlterTable()
    {
        var src = Path.Combine(RepoRoot.Path, "src");

        var migrationFiles = Directory.EnumerateDirectories(src, "Migrations", SearchOption.AllDirectories)
            .Where(dir => !IsBuildOutput(dir))
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .ToList();
        Assert.Empty(migrationFiles);

        var alterTable = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && Path.GetExtension(file) is ".cs" or ".sql")
            .Where(file => File.ReadAllText(file).Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .ToList();
        Assert.Empty(alterTable);
    }

    private static bool IsBuildOutput(string path)
        => path.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj");

    private sealed class ConnectionOpenRecorder : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Attempts++;
            return base.ConnectionOpening(connection, eventData, result);
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
        }
    }
}

// PLAN-0001 (CA-15): la suite corre completa. El único Skip admitido es el del CA-18 si Npgsql intentara conectarse
// (ADR-0001 §Decisión 5); cualquier otro test apagado tiene que fallar aquí en vez de pasar desapercibido.
public sealed class TestSuiteSkipPolicyTests
{
    private static readonly Regex SkippedTest = new(@"\[\s*(Fact|Theory)\s*\([^\]]*\bSkip\s*=", RegexOptions.Compiled);

    private const string AllowedFile = "tests/Zentory.Tests/Integration/Schema/PostgresModelTests.cs";

    [Fact]
    public void PLAN0001_CA15_Suite_SinTestsSkip_SalvoPostgresModel()
    {
        var tests = Path.Combine(RepoRoot.Path, "tests");

        var skipped = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"))
            .Where(file => SkippedTest.IsMatch(File.ReadAllText(Path.Combine(RepoRoot.Path, file))))
            .ToList();

        Assert.All(skipped, file => Assert.Equal(AllowedFile, file));
    }
}

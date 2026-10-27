using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Unit.Build;

// PLAN-0018 (RF-1, RF-8, RF-9; ADR-0007 §1, §5, §7): política estática de la carpeta de migraciones. Las migraciones
// viven en Infrastructure/Migrations, la primera es InitialCreate y las del plan siguen el orden de RF-9; ningún .cs ni
// .sql de src/ trae ALTER TABLE escrito a mano.
public sealed partial class MigrationsPolicyTests
{
    private const string MigrationsFolder    = "src/Zentory.Infrastructure/Migrations";
    private const string OldMigrationsFolder = "src/Zentory.Infrastructure/Persistence/Migrations";
    private const string SnapshotFile        = "ZentoryDbContextModelSnapshot.cs";
    private const string MigrationsNamespace = "Zentory.Infrastructure.Migrations";

    // Orden de RF-9 (architecture.md §3): M0 → M0b → M0c → M0d → M1 → M2 → M2b → M3a → M3 → M4 → M4b → M4c. Cada tarea
    // con migración agrega la suya en este orden; las de planes posteriores van después.
    private static readonly string[] PlanMigrationOrder =
    [
        "InitialCreate",
        "AddUserLastActiveOrganization",
        "AddOrganizationSoftDelete",
        "AddLegalDocumentArchive",
        "AddUserEmailVerification",
        "HashRefreshTokens",
        "AddUniqueUserGoogleId",
        "AddUserSessions",
        "AddDatabaseMarker",
        "AddActiveSubscriptionUniqueIndex",
        "AddOrganizationInvitations",
        "AddUniqueActiveMembership",
        "AddOrganizationOwnershipTransfers",
    ];

    [GeneratedRegex(@"^(?<stamp>\d{14})_(?<name>[A-Z][A-Za-z0-9]*)\.cs$")]
    private static partial Regex MigrationFileName();

    [Fact]
    public void PLAN0018_CA7_CarpetaDeMigraciones_InitialCreatePrimeraConSnapshotYOrdenDeRf9()
    {
        var folder = Path.Combine(RepoRoot.Path, MigrationsFolder);
        Assert.True(Directory.Exists(folder), $"No existe {MigrationsFolder}.");
        Assert.True(File.Exists(Path.Combine(folder, SnapshotFile)), $"Falta {SnapshotFile} en {MigrationsFolder}.");

        var files = Directory.EnumerateFiles(folder, "*.cs").Select(Path.GetFileName).OfType<string>().ToList();
        var migrations = files
            .Where(file => file != SnapshotFile && !file.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Select(file => (File: file, Match: MigrationFileName().Match(file)))
            .ToList();

        // Todo archivo es una migración con nombre <yyyyMMddHHmmss>_<PascalCase>.cs y su .Designer.cs.
        Assert.All(migrations, item => Assert.True(item.Match.Success, $"Nombre de migración inválido: {item.File}"));
        Assert.All(migrations, item => Assert.Contains(Path.ChangeExtension(item.File, ".Designer.cs"), files));
        Assert.All(migrations, item => Assert.True(DateTime.TryParseExact(item.Match.Groups["stamp"].Value,
            "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            $"Marca de tiempo inválida: {item.File}"));

        var ordered = migrations
            .OrderBy(item => item.Match.Groups["stamp"].Value, StringComparer.Ordinal)
            .Select(item => item.Match.Groups["name"].Value)
            .ToList();

        Assert.NotEmpty(ordered);
        Assert.Equal("InitialCreate", ordered[0]);
        Assert.Equal(ordered.Count, ordered.Distinct(StringComparer.Ordinal).Count());

        // Las migraciones del plan aparecen en el orden de RF-9 (las que existan hasta ahora).
        var planPrefix = ordered.Take(PlanMigrationOrder.Length).ToList();
        Assert.Equal(PlanMigrationOrder.Take(planPrefix.Count), planPrefix);

        Assert.False(Directory.Exists(Path.Combine(RepoRoot.Path, OldMigrationsFolder)),
            $"{OldMigrationsFolder} no debe existir (ADR-0007 §1).");
    }

    [Fact]
    public void PLAN0018_CA7_EnsambladoDeInfrastructure_MigracionesEnElNamespaceYEnOrdenCronologico()
    {
        using var db = new ZentoryDbContext(new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql("Host=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();

        Assert.NotNull(assembly.ModelSnapshot);
        Assert.Equal(MigrationsNamespace, assembly.ModelSnapshot!.GetType().Namespace);

        var ids = assembly.Migrations.Keys.ToList();
        Assert.NotEmpty(ids);
        Assert.EndsWith("_InitialCreate", ids[0], StringComparison.Ordinal);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
        Assert.All(assembly.Migrations.Values,
            type => Assert.Equal(MigrationsNamespace, type.Namespace));
        Assert.All(assembly.Migrations.Values,
            type => Assert.NotNull(type.GetCustomAttribute<MigrationAttribute>()));
    }

    [Fact]
    public void PLAN0018_CA14_Src_SinAlterTableEnCsNiSql()
    {
        var offending = AlterTableFiles(Path.Combine(RepoRoot.Path, "src"));

        Assert.True(offending.Count == 0, "ALTER TABLE escrito a mano en src/: " + string.Join(", ", offending));
    }

    [Fact]
    public void PLAN0018_CA14_DetectorDeAlterTable_SenalaCsYSql_IgnoraBinObjYOtrasExtensiones()
    {
        // Control del anterior sobre una carpeta temporal (no sobre src/).
        var root = Directory.CreateTempSubdirectory("zentory-alter-table-").FullName;
        try
        {
            Write(root, "Migrations/20260101000000_Bad.cs", "migrationBuilder.Sql(\"alter table core.users add x int\");");
            Write(root, "Scripts/fix.sql", "ALTER TABLE billing.invoices DROP COLUMN y;");
            Write(root, "Migrations/20260101000001_Good.cs", "migrationBuilder.AddColumn<int>(name: \"x\");");
            Write(root, "Notes/readme.md", "ALTER TABLE en un .md no cuenta");
            Write(root, "bin/Debug/Generated.cs", "ALTER TABLE generado");
            Write(root, "obj/Generated.sql", "ALTER TABLE generado");

            Assert.Equal(["Migrations/20260101000000_Bad.cs", "Scripts/fix.sql"], AlterTableFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static List<string> AlterTableFiles(string root)
        => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(file => !file.Split('/').Any(part => part is "bin" or "obj"))
            .Where(file => Path.GetExtension(file) is ".cs" or ".sql")
            .Where(file => File.ReadAllText(Path.Combine(root, file)).Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

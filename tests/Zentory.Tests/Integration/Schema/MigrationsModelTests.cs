using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Zentory.Infrastructure.Persistence;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Schema;

// PLAN-0018 (RF-8, ADR-0007 §6): la suite corre en InMemory, que ignora las migraciones. Estos tests comparan el modelo
// con la última migración sin base de datos: un cambio de configuración sin su migración rompe aquí y no en el primer
// Migrate() real. Reemplazan al test temporal PLAN0001_CA24_HastaPlan0018_*. Ninguno abre conexión.
public sealed class MigrationsModelTests
{
    [Fact]
    public void PLAN0018_CA8_ModeloActual_FrenteAlSnapshotDeLaUltimaMigracion_SinDiferenciasNiConexiones()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        // El snapshot es un modelo de diseño sin inicializar: hay que finalizarlo e inicializarlo como lo hace la
        // herramienta antes de compararlo.
        var snapshotModel = snapshot.Model is IMutableModel mutable ? mutable.FinalizeModel() : snapshot.Model;
        snapshotModel = db.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshotModel, designTime: true, validationLogger: null);

        var currentModel = db.GetService<IDesignTimeModel>().Model;
        var differ = db.GetService<IMigrationsModelDiffer>();

        var hasDifferences = differ.HasDifferences(
            snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        // Si falla, el mensaje dice qué operaciones faltan (nombres de tipo de operación, sin datos).
        var pending = hasDifferences
            ? differ.GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel())
                .Select(operation => operation.GetType().Name)
                .ToList()
            : [];
        Assert.False(hasDifferences,
            "El modelo tiene cambios sin migración; genera una con el comando de ADR-0007 §2. Operaciones: "
            + string.Join(", ", pending));
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA9_ScriptDeMigraciones_SinConexion_TieneUnCreateTablePorCadaTablaDelModelo()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();
        var sql = db.GetService<ISqlGenerationHelper>();

        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .Select(entity => (Name: entity.GetTableName()!, Schema: entity.GetSchema()))
            .Distinct()
            .ToList();
        Assert.NotEmpty(tables);

        var missingOrRepeated = tables
            .Select(table => (Table: sql.DelimitIdentifier(table.Name, table.Schema),
                Count: CountCreateTable(script, sql.DelimitIdentifier(table.Name, table.Schema))))
            .Where(item => item.Count != 1)
            .Select(item => $"{item.Table} ({item.Count})")
            .ToList();

        Assert.True(missingOrRepeated.Count == 0,
            "Tablas del modelo sin exactamente un CREATE TABLE en el script: " + string.Join(", ", missingOrRepeated));

        // Y el script no crea tablas que el modelo no tenga (salvo el historial de migraciones de EF).
        var createdTables = Regex.Matches(script, @"CREATE TABLE (?:IF NOT EXISTS )?(\S+) \(").Count;
        Assert.Equal(tables.Count + 1, createdTables);
        Assert.Equal(0, connections.Attempts);
    }

    private static int CountCreateTable(string script, string delimitedTable)
        => Regex.Matches(script, @"CREATE TABLE " + Regex.Escape(delimitedTable) + @" \(").Count;

    private static ZentoryDbContext CreateContext(ConnectionOpenRecorder connections)
    {
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
            .AddInterceptors(connections)
            .Options;

        return new ZentoryDbContext(options);
    }
}

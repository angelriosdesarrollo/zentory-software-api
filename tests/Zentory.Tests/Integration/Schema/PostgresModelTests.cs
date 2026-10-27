using Microsoft.EntityFrameworkCore;
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
}

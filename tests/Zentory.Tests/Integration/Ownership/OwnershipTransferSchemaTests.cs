using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Zentory.Domain.Entities;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Configurations.Core;
using Zentory.Infrastructure.Persistence.Purge;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Ownership;

// PLAN-0018 ★ M4c (AddOrganizationOwnershipTransfers; ADR-0007; architecture.md §3) sin conexión: una sola migración justo
// después de M4b, generada por la herramienta y sin ediciones a mano; la tabla con el índice único filtrado a las
// pendientes y el índice del destino; Status como token de concurrencia; filtro de tenant y clasificación "purgar".
public sealed class OwnershipTransferSchemaTests(ZentoryApiFactory factory) : IClassFixture<ZentoryApiFactory>
{
    private const string Migration         = "AddOrganizationOwnershipTransfers";
    private const string PreviousMigration = "AddUniqueActiveMembership";

    [Fact]
    public void PLAN0018_CA8_MigracionM4c_UnaSolaJustoDespuesDeM4bConLaTablaYSusDosIndicesSinSentenciasDeDatos()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var assembly = db.GetService<IMigrationsAssembly>();

        var names = assembly.Migrations.Keys.Select(id => id[(id.IndexOf('_') + 1)..]).ToList();
        Assert.Single(names, name => name == Migration);
        Assert.Equal(names.IndexOf(PreviousMigration) + 1, names.IndexOf(Migration));

        var migration = assembly.CreateMigration(
            assembly.Migrations.Single(m => m.Key.EndsWith("_" + Migration)).Value, db.Database.ProviderName!);
        Assert.Equal(
            [typeof(CreateTableOperation), typeof(CreateIndexOperation), typeof(CreateIndexOperation)],
            migration.UpOperations.Select(o => o.GetType()));
        var table = (CreateTableOperation)migration.UpOperations[0];
        Assert.Equal(("core", OrganizationOwnershipTransferConfiguration.Table), (table.Schema, table.Name));
        Assert.Equal(
            ["created_at", "expires_at", "from_user_id", "id", "organization_id", "preview_hash", "resolved_at", "status",
             "to_user_id", "updated_at"],
            table.Columns.Select(c => c.Name).Order(StringComparer.Ordinal));

        var pending = migration.UpOperations.OfType<CreateIndexOperation>()
            .Single(i => i.Name == OrganizationOwnershipTransferConfiguration.PendingIndex);
        Assert.Equal((true, OrganizationOwnershipTransferConfiguration.PendingFilter), (pending.IsUnique, pending.Filter));
        Assert.Equal(["organization_id"], pending.Columns);
        var toUser = migration.UpOperations.OfType<CreateIndexOperation>()
            .Single(i => i.Name == OrganizationOwnershipTransferConfiguration.ToUserStatusIndex);
        Assert.Equal(["to_user_id", "status"], toUser.Columns);
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA9_ScriptDeM4c_SinConexion_CreaElIndiceUnicoFiltradoALasPendientesSinDatos()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

        var m4c = db.GetService<IMigrator>().GenerateScript(
            fromMigration: ids.Single(id => id.EndsWith("_" + PreviousMigration, StringComparison.Ordinal)),
            toMigration:   ids.Single(id => id.EndsWith("_" + Migration, StringComparison.Ordinal)));

        Assert.Contains(
            $"CREATE UNIQUE INDEX {OrganizationOwnershipTransferConfiguration.PendingIndex} ON core.{OrganizationOwnershipTransferConfiguration.Table} (organization_id) WHERE {OrganizationOwnershipTransferConfiguration.PendingFilter};",
            m4c, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(UPDATE|DELETE FROM)\b"), m4c);
        Assert.Single(Regex.Matches(m4c, @"\bINSERT INTO\b"));
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA127_Modelo_StatusEsTokenDeConcurrenciaConFiltroDeTenantYSeClasificaParaPurgar()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZentoryDbContext>();
        var entity = db.Model.FindEntityType(typeof(OrganizationOwnershipTransfer))!;

        // SR-0018.37: aceptar, rechazar, cancelar y eliminar la organización compiten por Status.
        Assert.True(entity.FindProperty(nameof(OrganizationOwnershipTransfer.Status))!.IsConcurrencyToken);
        Assert.NotNull(entity.GetQueryFilter());
        Assert.Equal(OrganizationDataCategory.PurgeByOrganization,
            OrganizationDataCatalog.Default.CategoryOf(typeof(OrganizationOwnershipTransfer)));
    }

    private static ZentoryDbContext CreateContext(ConnectionOpenRecorder connections)
    {
        var options = new DbContextOptionsBuilder<ZentoryDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.MigrationsAssembly(typeof(ZentoryDbContext).Assembly.FullName))
            .AddInterceptors(connections)
            .Options;

        return new ZentoryDbContext(options);
    }
}

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Zentory.Domain.Constants;
using Zentory.Infrastructure.Persistence;
using Zentory.Infrastructure.Persistence.Configurations;
using Zentory.Infrastructure.Persistence.Configurations.Billing;
using Zentory.Infrastructure.Persistence.Configurations.Core;
using Zentory.Tests.Support;

namespace Zentory.Tests.Integration.Schema;

// PLAN-0018 CA-T15-1 (parte de CA-85, se cierra en T30; RF-27, M1): el script completo de IMigrator.GenerateScript(),
// sin conexión, deja users.email con su índice único y con el check de email normalizado, y la sentencia de datos de M1
// normaliza las filas existentes antes de crear el check. CA-T18-1 (M2b, RF-28 d, SR-0018.15): el script deja
// users.google_id con un índice único filtrado a los valores no nulos. CA-T18B-3 (M2c, RF-90 a): AddUserSessions va justo
// después de AddUniqueUserGoogleId, solo agrega users.current_session_id y refresh_tokens.session_id (uuid nulos, sin
// relleno) y su tramo del script no tiene sentencias de datos. CA-T24-2 (M3, RF-38, C-1): ux_subscriptions_user_active único
// parcial por status IN ('active','trialing') e ix_subscriptions_user_id no único, en una sola migración tras M3a.
// T29 (M4, RF-49 b): core.organization_invitations con token_hash único e índice (organization_id, email), en una sola
// migración AddOrganizationInvitations justo después de M3, sin sentencias de datos. T30 (M4b, RF-48 e, SR-0018.8; cierra
// CA-85): organization_members (organization_id, user_id) único filtrado a las vigentes, en una sola migración
// AddUniqueActiveMembership justo después de M4, con la sentencia de datos que borra los duplicados vigentes antes.
public sealed class MigrationScriptTests
{
    private const string UniqueEmailIndex = "ix_users_email";

    [Fact]
    public void PLAN0018_CA85_ScriptDeMigraciones_SinConexion_TieneCheckDeEmailNormalizadoJuntoAlIndiceUnicoDeUsersEmail()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();

        Assert.Matches(new Regex(
            $@"CREATE UNIQUE INDEX {UniqueEmailIndex} ON users \(email\);"), script);
        Assert.Matches(new Regex(
            $@"ALTER TABLE users ADD CONSTRAINT {UserConfiguration.EmailNormalizedCheck} CHECK \(email = lower\(btrim\(email\)\)\);"),
            script);
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA85_ScriptDeMigraciones_NormalizaLosEmailsExistentesAntesDeCrearElCheck()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();

        var update = script.IndexOf("UPDATE users SET email = lower(btrim(email))", StringComparison.Ordinal);
        var check  = script.IndexOf($"ADD CONSTRAINT {UserConfiguration.EmailNormalizedCheck}", StringComparison.Ordinal);
        Assert.True(update >= 0, "El script no normaliza los emails existentes.");
        Assert.True(check > update, "El check de email normalizado se crea antes de normalizar las filas existentes.");

        // El índice único se conserva: un solo índice sobre users.email, y es único.
        Assert.Single(Regex.Matches(script, @"CREATE (UNIQUE )?INDEX \S+ ON users \(email\);"));
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA85_ScriptDeMigraciones_SinConexion_DejaIndiceUnicoFiltradoEnUsersGoogleId()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();

        // InitialCreate lo creó no único; la última creación del índice (M2b) es la que queda y es única y filtrada.
        var creations = Regex.Matches(script,
            $@"CREATE (UNIQUE )?INDEX {UserConfiguration.GoogleIdIndex} ON users \(google_id\)[^;]*;");
        Assert.NotEmpty(creations);
        Assert.Equal(
            $"CREATE UNIQUE INDEX {UserConfiguration.GoogleIdIndex} ON users (google_id) WHERE google_id IS NOT NULL;",
            creations[^1].Value);
        Assert.Equal(0, connections.Attempts);
    }

    // ── M2c AddUserSessions (CA-T18B-3; RF-90 a, D-47) ─────────────────────────────────────────────────────────

    private const string GoogleIdMigration = "AddUniqueUserGoogleId";
    private const string SessionsMigration = "AddUserSessions";

    [Fact]
    public void PLAN0018_CA8_MigracionM2c_UnaSolaAddUserSessionsJustoDespuesDeAddUniqueUserGoogleIdSoloConColumnasNuevas()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var assembly = db.GetService<IMigrationsAssembly>();

        var names = assembly.Migrations.Keys.Select(id => id[(id.IndexOf('_') + 1)..]).ToList();
        Assert.Single(names, name => name == SessionsMigration);
        Assert.Equal(names.IndexOf(GoogleIdMigration) + 1, names.IndexOf(SessionsMigration));

        // Sin edición a mano: solo las dos columnas que genera la herramienta, sin sentencias de datos ni índices.
        var migration = assembly.CreateMigration(assembly.Migrations.Single(m => m.Key.EndsWith("_" + SessionsMigration)).Value,
            db.Database.ProviderName!);
        var columns = migration.UpOperations.Select(Assert.IsType<AddColumnOperation>).ToList();
        Assert.Equal(
            [("users", "current_session_id"), ("refresh_tokens", "session_id")],
            columns.Select(c => (c.Table, c.Name)));
        Assert.All(columns, column =>
        {
            Assert.True(column.IsNullable);
            Assert.Equal(typeof(Guid), column.ClrType);
            Assert.Null(column.DefaultValue);
            Assert.Null(column.DefaultValueSql);
        });
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA9_ScriptDeMigraciones_SinConexion_TieneCurrentSessionIdEnUsersYSessionIdEnRefreshTokensSinSentenciasDeDatos()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var migrator = db.GetService<IMigrator>();
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

        var script = migrator.GenerateScript();
        Assert.Matches(new Regex(@"ALTER TABLE users ADD current_session_id uuid;"), script);
        Assert.Matches(new Regex(@"ALTER TABLE refresh_tokens ADD session_id uuid;"), script);

        // El tramo de M2c: solo los ALTER TABLE y el registro en el historial de migraciones.
        var m2c = migrator.GenerateScript(
            fromMigration: ids.Single(id => id.EndsWith("_" + GoogleIdMigration, StringComparison.Ordinal)),
            toMigration:   ids.Single(id => id.EndsWith("_" + SessionsMigration, StringComparison.Ordinal)));
        Assert.DoesNotMatch(new Regex(@"\b(UPDATE|DELETE FROM)\b"), m2c);
        Assert.Single(Regex.Matches(m2c, @"\bINSERT INTO\b"));
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", m2c, StringComparison.Ordinal);
        Assert.Equal(0, connections.Attempts);
    }

    // ── M3 AddActiveSubscriptionUniqueIndex (CA-T24-2; RF-38, C-1) ──────────────────────────────────────────────

    private const string MarkerMigration       = "AddDatabaseMarker";
    private const string SubscriptionMigration = "AddActiveSubscriptionUniqueIndex";

    [Fact]
    public void PLAN0018_CA85_ScriptDeMigraciones_SinConexion_TieneUxSubscriptionsUserActiveUnicoParcialYIxSubscriptionsUserIdNoUnico()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();

        // Índice único parcial con los nombres de columna reales (user_id, status) y los valores de SubscriptionStatus.
        var active = Regex.Matches(script,
            $@"CREATE (UNIQUE )?INDEX {SubscriptionConfiguration.ActiveUserIndex} ON billing\.subscriptions \([^)]*\)[^;]*;");
        Assert.Equal(
            $"CREATE UNIQUE INDEX {SubscriptionConfiguration.ActiveUserIndex} ON billing.subscriptions (user_id) " +
            $"WHERE status IN ('{SubscriptionStatus.Active}','{SubscriptionStatus.Trialing}');",
            Assert.Single(active).Value);

        // InitialCreate lo creó único; la última creación (M3) es la que queda y no es única ni filtrada.
        var byUser = Regex.Matches(script,
            $@"CREATE (UNIQUE )?INDEX {SubscriptionConfiguration.UserIdIndex} ON billing\.subscriptions \([^)]*\)[^;]*;");
        Assert.True(byUser.Count >= 2, "M3 no recrea ix_subscriptions_user_id.");
        Assert.Equal(
            $"CREATE INDEX {SubscriptionConfiguration.UserIdIndex} ON billing.subscriptions (user_id);",
            byUser[^1].Value);
        Assert.True(
            script.IndexOf($"DROP INDEX billing.{SubscriptionConfiguration.UserIdIndex};", StringComparison.Ordinal)
                < byUser[^1].Index,
            "M3 no elimina el índice único anterior antes de recrearlo no único.");
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA8_MigracionM3_UnaSolaAddActiveSubscriptionUniqueIndexJustoDespuesDeAddDatabaseMarkerSoloConIndices()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var assembly = db.GetService<IMigrationsAssembly>();

        var names = assembly.Migrations.Keys.Select(id => id[(id.IndexOf('_') + 1)..]).ToList();
        Assert.Single(names, name => name == SubscriptionMigration);
        Assert.Equal(names.IndexOf(MarkerMigration) + 1, names.IndexOf(SubscriptionMigration));

        // Sin edición a mano: solo el cambio de índices que genera la herramienta, sin sentencias de datos (sin datos que
        // migrar: el índice único anterior ya garantizaba una fila por usuario).
        var migration = assembly.CreateMigration(
            assembly.Migrations.Single(m => m.Key.EndsWith("_" + SubscriptionMigration)).Value, db.Database.ProviderName!);
        Assert.Equal(
            [typeof(DropIndexOperation), typeof(CreateIndexOperation), typeof(CreateIndexOperation)],
            migration.UpOperations.Select(o => o.GetType()));
        Assert.All(migration.UpOperations, operation =>
            Assert.Equal("subscriptions", operation is DropIndexOperation drop ? drop.Table : ((CreateIndexOperation)operation).Table));
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA9_ScriptDeM3_SinConexion_SoloIndicesSinSentenciasDeDatos()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

        var m3 = db.GetService<IMigrator>().GenerateScript(
            fromMigration: ids.Single(id => id.EndsWith("_" + MarkerMigration, StringComparison.Ordinal)),
            toMigration:   ids.Single(id => id.EndsWith("_" + SubscriptionMigration, StringComparison.Ordinal)));

        Assert.DoesNotMatch(new Regex(@"\b(UPDATE|DELETE FROM)\b"), m3);
        Assert.Single(Regex.Matches(m3, @"\bINSERT INTO\b"));
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", m3, StringComparison.Ordinal);
        Assert.Contains(SubscriptionConfiguration.ActiveUserIndex, m3, StringComparison.Ordinal);
        Assert.Equal(0, connections.Attempts);
    }

    // ── M4 (T29): AddOrganizationInvitations ────────────────────────────────────────────────────────────────────

    private const string InvitationsMigration = "AddOrganizationInvitations";

    [Fact]
    public void PLAN0018_CA85_ScriptDeMigraciones_SinConexion_TieneOrganizationInvitationsConTokenHashUnicoEIndiceOrgEmail()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();
        var table  = $"core.{OrganizationInvitationConfiguration.Table}";

        Assert.Contains($"CREATE TABLE {table} (", script, StringComparison.Ordinal);
        Assert.Contains(
            $"CREATE UNIQUE INDEX {OrganizationInvitationConfiguration.TokenHashIndex} ON {table} (token_hash);",
            script, StringComparison.Ordinal);
        Assert.Contains(
            $"CREATE INDEX {OrganizationInvitationConfiguration.OrganizationEmailIndex} ON {table} (organization_id, email);",
            script, StringComparison.Ordinal);
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA8_MigracionM4_UnaSolaAddOrganizationInvitationsJustoDespuesDeM3SoloConTablaEIndices()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var assembly = db.GetService<IMigrationsAssembly>();

        var names = assembly.Migrations.Keys.Select(id => id[(id.IndexOf('_') + 1)..]).ToList();
        Assert.Single(names, name => name == InvitationsMigration);
        Assert.Equal(names.IndexOf(SubscriptionMigration) + 1, names.IndexOf(InvitationsMigration));

        // Sin edición a mano: la tabla y sus dos índices, tal como los genera la herramienta.
        var migration = assembly.CreateMigration(
            assembly.Migrations.Single(m => m.Key.EndsWith("_" + InvitationsMigration)).Value, db.Database.ProviderName!);
        Assert.Equal(
            [typeof(CreateTableOperation), typeof(CreateIndexOperation), typeof(CreateIndexOperation)],
            migration.UpOperations.Select(o => o.GetType()));
        Assert.All(migration.UpOperations, operation => Assert.Equal(OrganizationInvitationConfiguration.Table,
            operation is CreateTableOperation create ? create.Name : ((CreateIndexOperation)operation).Table));
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA9_ScriptDeM4_SinConexion_SinSentenciasDeDatos()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

        var m4 = db.GetService<IMigrator>().GenerateScript(
            fromMigration: ids.Single(id => id.EndsWith("_" + SubscriptionMigration, StringComparison.Ordinal)),
            toMigration:   ids.Single(id => id.EndsWith("_" + InvitationsMigration, StringComparison.Ordinal)));

        Assert.DoesNotMatch(new Regex(@"\b(UPDATE|DELETE FROM)\b"), m4);
        Assert.Single(Regex.Matches(m4, @"\bINSERT INTO\b"));
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", m4, StringComparison.Ordinal);
        Assert.Contains(OrganizationInvitationConfiguration.TokenHashIndex, m4, StringComparison.Ordinal);
        Assert.Equal(0, connections.Attempts);
    }

    // ── M4b (T30): AddUniqueActiveMembership ────────────────────────────────────────────────────────────────────

    private const string ActiveMembershipMigration = "AddUniqueActiveMembership";

    [Fact]
    public void PLAN0018_CA85_ScriptDeMigraciones_SinConexion_TieneIndiceUnicoDeOrganizationMembersFiltradoALasVigentes()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);

        var script = db.GetService<IMigrator>().GenerateScript();
        var table  = $"core.{OrganizationMemberConfiguration.Table}";

        // InitialCreate lo creó no único; la última creación (M4b) es la que queda: única y solo sobre las vigentes.
        var creations = Regex.Matches(script,
            $@"CREATE (UNIQUE )?INDEX {OrganizationMemberConfiguration.OrgUserIndex} ON {Regex.Escape(table)} \([^)]*\)[^;]*;");
        Assert.True(creations.Count >= 2, "M4b no recrea ix_org_members_org_user.");
        Assert.Equal(
            $"CREATE UNIQUE INDEX {OrganizationMemberConfiguration.OrgUserIndex} ON {table} (organization_id, user_id) " +
            $"WHERE {OrganizationMemberConfiguration.ActiveFilter};",
            creations[^1].Value);
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA8_MigracionM4b_UnaSolaAddUniqueActiveMembershipJustoDespuesDeM4ConSentenciaDeDatosAntesDelIndice()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var assembly = db.GetService<IMigrationsAssembly>();

        var names = assembly.Migrations.Keys.Select(id => id[(id.IndexOf('_') + 1)..]).ToList();
        Assert.Single(names, name => name == ActiveMembershipMigration);
        Assert.Equal(names.IndexOf(InvitationsMigration) + 1, names.IndexOf(ActiveMembershipMigration));

        // La única edición a mano es la sentencia de datos antepuesta (ADR-0007 §5); el resto es el cambio de índice que
        // genera la herramienta, sobre organization_members.
        var migration = assembly.CreateMigration(
            assembly.Migrations.Single(m => m.Key.EndsWith("_" + ActiveMembershipMigration)).Value, db.Database.ProviderName!);
        Assert.Equal(
            [typeof(SqlOperation), typeof(DropIndexOperation), typeof(CreateIndexOperation)],
            migration.UpOperations.Select(o => o.GetType()));
        var sql = ((SqlOperation)migration.UpOperations[0]).Sql;
        Assert.Matches(new Regex(@"^\s*UPDATE core\.organization_members\b"), sql);
        Assert.DoesNotMatch(new Regex(@"\b(DELETE|INSERT|CREATE|DROP|ALTER)\b", RegexOptions.IgnoreCase), sql);
        var create = (CreateIndexOperation)migration.UpOperations[2];
        Assert.Equal((OrganizationMemberConfiguration.Table, true, OrganizationMemberConfiguration.ActiveFilter),
            (create.Table, create.IsUnique, create.Filter));
        Assert.Equal(OrganizationMemberConfiguration.Table, ((DropIndexOperation)migration.UpOperations[1]).Table);
        Assert.Equal(0, connections.Attempts);
    }

    [Fact]
    public void PLAN0018_CA9_ScriptDeM4b_SinConexion_BorraLosDuplicadosVigentesMasNuevosSinTocarAlOwnerAntesDelIndiceUnico()
    {
        var connections = new ConnectionOpenRecorder();
        using var db = CreateContext(connections);
        var ids = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();

        var m4b = db.GetService<IMigrator>().GenerateScript(
            fromMigration: ids.Single(id => id.EndsWith("_" + InvitationsMigration, StringComparison.Ordinal)),
            toMigration:   ids.Single(id => id.EndsWith("_" + ActiveMembershipMigration, StringComparison.Ordinal)));

        // Una sola sentencia de datos, un UPDATE que solo marca deleted_at (borrado lógico), nunca DELETE.
        Assert.DoesNotMatch(new Regex(@"\bDELETE FROM\b"), m4b);
        var update = Assert.Single(Regex.Matches(m4b, @"\bUPDATE core\.organization_members AS m\s+SET deleted_at = now\(\), updated_at = now\(\)"));
        var index = m4b.IndexOf($"CREATE UNIQUE INDEX {OrganizationMemberConfiguration.OrgUserIndex}", StringComparison.Ordinal);
        Assert.True(index > update.Index, "El índice único se crea antes de resolver los duplicados vigentes.");

        // Solo entre vigentes del mismo (org, user); conserva primero la membresía owner del OwnerId y luego la más antigua.
        Assert.Contains("WHERE om.deleted_at IS NULL", m4b, StringComparison.Ordinal);
        Assert.Contains("PARTITION BY om.organization_id, om.user_id", m4b, StringComparison.Ordinal);
        Assert.Contains($"ORDER BY (om.user_id = o.owner_id AND om.role = '{OrgRole.Owner}') IS TRUE DESC,", m4b,
            StringComparison.Ordinal);
        Assert.Matches(new Regex(@"om\.created_at, om\.id\) AS rn"), m4b);
        Assert.Contains("WHERE m.id = ranked.id AND ranked.rn > 1;", m4b, StringComparison.Ordinal);

        Assert.Single(Regex.Matches(m4b, @"\bINSERT INTO\b"));
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", m4b, StringComparison.Ordinal);
        Assert.Equal(0, connections.Attempts);
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

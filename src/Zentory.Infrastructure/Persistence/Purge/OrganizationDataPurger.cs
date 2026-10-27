using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Zentory.Application.Common.Interfaces;
using Zentory.Application.Organization.Commands;
using Zentory.Domain.Entities;
using Zentory.Domain.Repositories;

namespace Zentory.Infrastructure.Persistence.Purge;

// Purga física de una organización (PLAN-0018 RF-78; ADR purga rev. 2.2 §3 y §4; SR-0018.27, SR-0018.34; R-13). Es la
// entrada Persistence/Purge/ de la lista blanca de IgnoreQueryFilters: ve las filas con borrado lógico y las
// organizaciones eliminadas, y por eso cada borrado lleva el predicado explícito OrganizationId == orgId (o la clave de
// un padre de esa organización). Nunca usa ExecuteDelete (EF InMemory no lo soporta): lotes de RemoveRange +
// SaveChanges, la misma ruta en InMemory y en PostgreSQL. Lo que se hace con cada tipo sale de OrganizationDataCatalog.
public sealed class OrganizationDataPurger : IOrganizationDataPurger
{
    public const int    BatchSize   = 500;
    public const string AuditAction = "PURGE";

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly ZentoryDbContext        _db;
    private readonly IUnitOfWork             _uow;
    private readonly IStorageService         _storage;
    private readonly LegalDocumentArchiver   _archiver;
    private readonly OrganizationDataCatalog _catalog;

    public OrganizationDataPurger(
        ZentoryDbContext      db,
        IUnitOfWork           uow,
        IStorageService       storage,
        LegalDocumentArchiver archiver)
    {
        _db       = db;
        _uow      = uow;
        _storage  = storage;
        _archiver = archiver;
        _catalog  = OrganizationDataCatalog.Default;
    }

    // ── Paso 1: inicio con bloqueo ──────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Guid>> StartDuePurgesAsync(DateTime cutoffUtc, DateTime nowUtc, CancellationToken ct = default)
    {
        var candidates = await _db.Organizations.IgnoreQueryFilters()
            .Where(o => o.DeletedAt != null && (o.PurgeStartedAt != null || o.DeletedAt <= cutoffUtc))
            .OrderBy(o => o.DeletedAt)
            .ToListAsync(ct);

        var started = new List<Guid>();
        foreach (var organization in candidates)
        {
            if (organization.PurgeStartedAt is not null)
            {
                started.Add(organization.OrganizationId);
                continue;
            }

            // El sello leído viaja como token: si una restauración lo regeneró entre la lectura y este guardado, falla
            // por concurrencia y la organización se salta (CA-107, D). Ningún otro paso empieza sin esta confirmación.
            organization.StartPurge(nowUtc);
            try
            {
                await _db.SaveChangesAsync(ct);
                started.Add(organization.OrganizationId);
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(organization).State = EntityState.Detached;
            }
        }

        _db.ChangeTracker.Clear();
        return started;
    }

    // ── Pasos 2 a 6 ─────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<OrganizationPurgeReport> PurgeAsync(Guid organizationId, DateTime nowUtc, CancellationToken ct = default)
    {
        // 2. Identificador válido, antes de leer o borrar nada.
        if (organizationId == Guid.Empty)
            throw new ArgumentException("La purga necesita una organización.", nameof(organizationId));

        var organization = await _db.Organizations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(o => o.OrganizationId == organizationId, ct);
        if (organization.DeletedAt is null || organization.PurgeStartedAt is null)
            throw new InvalidOperationException("Solo se purga una organización eliminada con la purga iniciada.");

        // 3. Archivo legal, mientras las filas y los PDF siguen bajo {orgId}/.
        var archived = await _archiver.ArchiveAsync(organizationId, nowUtc, ct);

        // 4. Filas, según el catálogo y en este orden (ADR purga §4.4).
        var rows = new SortedDictionary<string, int>(StringComparer.Ordinal);
        Count(rows, typeof(CollaboratorAccessToken), await DeleteOrphanCollaboratorTokensAsync(organizationId, ct));
        foreach (var type in _catalog.Detach)
            Count(rows, type, await InvokeAsync(nameof(DetachAsync), type, organizationId, ct));
        Count(rows, typeof(User), await DetachUsersAsync(organizationId, ct));
        foreach (var link in _catalog.PurgeByParent)
            Count(rows, link.Child, await InvokeAsync(nameof(DeleteByParentAsync), link.Child, organizationId, ct, link));
        foreach (var type in DependentsFirst(_catalog.PurgeByOrganization))
            Count(rows, type, await InvokeAsync(nameof(DeleteByOrganizationAsync), type, organizationId, ct));

        // 5. Archivos, después de las filas (SR-0018.27 d). El prefijo se valida dentro: exactamente "{orgId}/".
        var files = await _storage.DeleteOrganizationFilesAsync(OrganizationStoragePrefix.For(organizationId), ct);

        // 6. Cierre: la fila de Organization (con PurgeStartedAt) es lo último que se borra, junto con el AuditLog de la
        // purga, sin nombre, NIT ni contenido del tenant.
        var report = new OrganizationPurgeReport(organizationId, rows, files, archived);
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var row = await _db.Organizations.IgnoreQueryFilters()
                .SingleAsync(o => o.OrganizationId == organizationId, token);
            _db.Organizations.Remove(row);
            _db.AuditLogs.Add(AuditLog.Record(
                tableName:      TableName(typeof(Organization)),
                recordId:       organizationId,
                action:         AuditAction,
                organizationId: organizationId,
                newValues:      AuditValues(report, nowUtc)));
            await _db.SaveChangesAsync(token);
        }, ct);
        _db.ChangeTracker.Clear();

        return report;
    }

    // ── Archivo legal vencido ───────────────────────────────────────────────────────────────────────────────────

    public async Task<int> DeleteExpiredArchiveAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var total = 0;
        while (true)
        {
            var batch = await _db.LegalDocumentArchives
                .Where(a => a.RetainUntil < nowUtc)
                .OrderBy(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                return total;

            // El PDF primero: si el almacenamiento falla, la fila sigue y la corrida siguiente lo reintenta.
            foreach (var key in batch.Select(a => a.PdfStorageKey).OfType<string>())
                await _storage.DeleteAsync(key, ct);

            _db.LegalDocumentArchives.RemoveRange(batch);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    // ── Filas ───────────────────────────────────────────────────────────────────────────────────────────────────

    // 4.1 (SR-0018.34): CollaboratorAccessToken no tiene OrganizationId; va por email normalizado. Se borran los de los
    // colaboradores de la organización que no tienen otra colaboración vigente (otro Collaborator con el mismo email, sin
    // borrado lógico, en otra organización no eliminada). Va primero porque los emails salen de filas que se borran
    // después.
    private async Task<int> DeleteOrphanCollaboratorTokensAsync(Guid organizationId, CancellationToken ct)
    {
        var emails = (await _db.Collaborators.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.OrganizationId == organizationId && c.Email != null)
                .Select(c => c.Email!)
                .ToListAsync(ct))
            .Select(email => email.Trim().ToLowerInvariant())
            .Where(email => email.Length > 0)
            .Distinct()
            .ToList();
        if (emails.Count == 0)
            return 0;

        // _db.Organizations sin IgnoreQueryFilters: su filtro global (DeletedAt == null) es justo "no eliminada".
        var stillCollaborating = (await _db.Collaborators.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.OrganizationId != organizationId
                    && c.DeletedAt == null
                    && c.Email != null
                    && emails.Contains(c.Email.Trim().ToLower())
                    && _db.Organizations.Any(o => o.OrganizationId == c.OrganizationId))
                .Select(c => c.Email!)
                .ToListAsync(ct))
            .Select(email => email.Trim().ToLowerInvariant())
            .ToHashSet();

        var orphans = emails.Where(email => !stillCollaborating.Contains(email)).ToList();
        if (orphans.Count == 0)
            return 0;

        return await DeleteInBatchesAsync(_db.CollaboratorAccessTokens.Where(t => orphans.Contains(t.Email)), ct);
    }

    // 4.2 (C-6): facturación del owner, desvinculada (OrganizationId = null); la fila se conserva.
    private async Task<int> DetachAsync<TEntity>(Guid organizationId, CancellationToken ct) where TEntity : class
    {
        var total = 0;
        while (true)
        {
            var batch = await OrderedByKey(ByOrganization<TEntity>(organizationId)).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
                return total;
            foreach (var entity in batch)
                _db.Entry(entity).Property(OrganizationDataCatalog.OrganizationIdProperty).CurrentValue = null;
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    // 4.3: los usuarios que apuntan a la organización quedan sin ella; el usuario no se toca más (es del usuario).
    private async Task<int> DetachUsersAsync(Guid organizationId, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var batch = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.OrganizationId == organizationId || u.LastActiveOrganizationId == organizationId)
                .OrderBy(u => u.UserId)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                return total;
            foreach (var user in batch)
            {
                var entry = _db.Entry(user);
                if (user.OrganizationId == organizationId)
                    entry.Property(u => u.OrganizationId).CurrentValue = null;
                if (user.LastActiveOrganizationId == organizationId)
                    entry.Property(u => u.LastActiveOrganizationId).CurrentValue = null;
            }
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    // 4.4: hijos sin OrganizationId, por la clave de un padre de esta organización, antes que el padre.
    private async Task<int> DeleteByParentAsync<TEntity>(Guid organizationId, CancellationToken ct, ParentLink link)
        where TEntity : class
    {
        var parentKey = SingleGuidKey(link.Parent);
        var parentIds = await (Task<List<Guid>>)GenericMethod(nameof(ParentKeysAsync), link.Parent)
            .Invoke(this, [organizationId, parentKey, ct])!;

        var foreignKey = _db.Model.FindEntityType(typeof(TEntity))!.FindProperty(link.ForeignKey)
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} no tiene {link.ForeignKey}.");

        var total = 0;
        foreach (var chunk in parentIds.Chunk(BatchSize))
            total += await DeleteInBatchesAsync(
                _db.Set<TEntity>().IgnoreQueryFilters().Where(KeyIn<TEntity>(foreignKey, chunk)), ct);
        return total;
    }

    private Task<List<Guid>> ParentKeysAsync<TParent>(Guid organizationId, string parentKey, CancellationToken ct)
        where TParent : class
        => ByOrganization<TParent>(organizationId).Select(p => EF.Property<Guid>(p, parentKey)).ToListAsync(ct);

    // 4.5: el resto, por el predicado explícito OrganizationId == orgId.
    private Task<int> DeleteByOrganizationAsync<TEntity>(Guid organizationId, CancellationToken ct) where TEntity : class
        => DeleteInBatchesAsync(ByOrganization<TEntity>(organizationId), ct);

    private async Task<int> DeleteInBatchesAsync<TEntity>(IQueryable<TEntity> query, CancellationToken ct) where TEntity : class
    {
        var total = 0;
        while (true)
        {
            var batch = await OrderedByKey(query).Take(BatchSize).ToListAsync(ct);
            if (batch.Count == 0)
                return total;
            _db.Set<TEntity>().RemoveRange(batch);
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            total += batch.Count;
        }
    }

    // ── Predicados y orden ──────────────────────────────────────────────────────────────────────────────────────

    // Set<T>().IgnoreQueryFilters().Where(e => EF.Property<Guid|Guid?>(e, "OrganizationId") == orgId), con el Id como
    // parámetro de la consulta.
    private IQueryable<TEntity> ByOrganization<TEntity>(Guid organizationId) where TEntity : class
    {
        var property = _db.Model.FindEntityType(typeof(TEntity))!.FindProperty(OrganizationDataCatalog.OrganizationIdProperty)
            ?? throw new InvalidOperationException(
                $"{typeof(TEntity).Name} no tiene {OrganizationDataCatalog.OrganizationIdProperty}.");

        var entity = Expression.Parameter(typeof(TEntity), "e");
        var value  = Expression.Property(Expression.Constant(new GuidParameter(organizationId)), nameof(GuidParameter.Value));
        var body   = Expression.Equal(
            PropertyAccess(entity, property),
            property.ClrType == typeof(Guid) ? value : Expression.Convert(value, property.ClrType));

        return _db.Set<TEntity>().IgnoreQueryFilters().Where(Expression.Lambda<Func<TEntity, bool>>(body, entity));
    }

    private static Expression<Func<TEntity, bool>> KeyIn<TEntity>(IProperty property, Guid[] ids)
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        Expression values = property.ClrType == typeof(Guid)
            ? Expression.Constant(ids.ToList())
            : Expression.Constant(ids.Select(id => (Guid?)id).ToList());
        var contains = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [property.ClrType],
            values, PropertyAccess(entity, property));
        return Expression.Lambda<Func<TEntity, bool>>(contains, entity);
    }

    private static MethodCallExpression PropertyAccess(ParameterExpression entity, IProperty property)
        => Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType], entity, Expression.Constant(property.Name));

    // Lotes estables: orden por la clave primaria (sin OrderBy, Take es no determinista y EF lo advierte).
    private IQueryable<TEntity> OrderedByKey<TEntity>(IQueryable<TEntity> query) where TEntity : class
    {
        var key = _db.Model.FindEntityType(typeof(TEntity))!.FindPrimaryKey()!.Properties;
        var expression = query.Expression;
        for (var i = 0; i < key.Count; i++)
        {
            var entity = Expression.Parameter(typeof(TEntity), "e");
            var lambda = Expression.Lambda(PropertyAccess(entity, key[i]), entity);
            expression = Expression.Call(typeof(Queryable), i == 0 ? nameof(Queryable.OrderBy) : nameof(Queryable.ThenBy),
                [typeof(TEntity), key[i].ClrType], expression, Expression.Quote(lambda));
        }
        return query.Provider.CreateQuery<TEntity>(expression);
    }

    // Orden de borrado por el grafo de FK del modelo: un tipo va antes que los tipos de los que depende (ADR purga §3).
    private List<Type> DependentsFirst(IReadOnlyList<Type> types)
    {
        var set = types.ToHashSet();
        var principalsOf = types.ToDictionary(
            type => type,
            type => _db.Model.FindEntityType(type)!.GetForeignKeys()
                .Select(fk => fk.PrincipalEntityType.ClrType)
                .Where(principal => principal != type && set.Contains(principal))
                .ToHashSet());

        var ordered = new List<Type>();
        var remaining = types.ToList();
        while (remaining.Count > 0)
        {
            // Listo: ningún pendiente depende de él (todos sus dependientes ya están en la lista).
            var ready = remaining.FirstOrDefault(type =>
                !remaining.Any(other => other != type && principalsOf[other].Contains(type)));
            if (ready is null)
                throw new InvalidOperationException(
                    "El catálogo de purga tiene un ciclo de FK: " + string.Join(", ", remaining.Select(t => t.Name)));
            ordered.Add(ready);
            remaining.Remove(ready);
        }
        return ordered;
    }

    // ── Apoyo ───────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<int> InvokeAsync(string method, Type entityType, Guid organizationId, CancellationToken ct, params object[] extra)
        => (Task<int>)GenericMethod(method, entityType).Invoke(this, [organizationId, ct, .. extra])!;

    private static MethodInfo GenericMethod(string name, Type entityType)
        => typeof(OrganizationDataPurger).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityType);

    private string SingleGuidKey(Type type)
    {
        var key = _db.Model.FindEntityType(type)!.FindPrimaryKey()!.Properties;
        return key is [{ ClrType: var clrType } property] && clrType == typeof(Guid)
            ? property.Name
            : throw new InvalidOperationException($"El padre {type.Name} necesita una clave primaria Guid simple.");
    }

    private string TableName(Type type) => _db.Model.FindEntityType(type)!.GetTableName() ?? type.Name;

    private void Count(IDictionary<string, int> rows, Type type, int count)
    {
        if (count > 0)
            rows[TableName(type)] = rows.TryGetValue(TableName(type), out var previous) ? previous + count : count;
    }

    // Solo conteos y fechas: ni nombre, ni NIT, ni contenido del tenant (RF-78 6).
    private static string AuditValues(OrganizationPurgeReport report, DateTime nowUtc)
        => JsonSerializer.Serialize(new
        {
            purgedAt          = nowUtc,
            rowsByTable       = report.RowsByTable,
            filesDeleted      = report.FilesDeleted,
            documentsArchived = report.DocumentsArchived,
        }, AuditJson);

    // Contenedor del Id para que EF lo envíe como parámetro y no como literal en el SQL.
    private sealed class GuidParameter(Guid value)
    {
        public Guid Value { get; } = value;
    }
}

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Zentory.Infrastructure.Persistence;

namespace Zentory.Tests.Support;

// Filas por tabla de todo el modelo (PLAN-0018 CA-42, CA-43; SeedCliTests y AssignPlanBySeedTests): una entrada por tipo
// de entidad con clave, por nombre del tipo, sin filtros globales. Comparar dos conteos dice qué tablas cambió una
// corrida, sin depender de una lista de tablas que se quede atrás cuando el modelo crece.
internal static class TableRowCounts
{
    private static readonly MethodInfo CountOfMethod =
        typeof(TableRowCounts).GetMethod(nameof(CountOf), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static async Task<Dictionary<string, int>> CountAsync(ZentoryDbContext db)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var entities = db.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType && e.FindPrimaryKey() is not null)
            .OrderBy(e => e.ClrType.Name, StringComparer.Ordinal);

        foreach (var entity in entities)
            counts[entity.ClrType.Name] =
                await (Task<int>)CountOfMethod.MakeGenericMethod(entity.ClrType).Invoke(null, [db])!;

        return counts;
    }

    // Tablas cuyo conteo cambió entre dos fotos del mismo modelo.
    public static IReadOnlyList<string> Changed(IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after)
        => before.Keys.Union(after.Keys)
            .Where(table => before.GetValueOrDefault(table) != after.GetValueOrDefault(table))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Task<int> CountOf<TEntity>(ZentoryDbContext db) where TEntity : class
        => db.Set<TEntity>().IgnoreQueryFilters().CountAsync();
}

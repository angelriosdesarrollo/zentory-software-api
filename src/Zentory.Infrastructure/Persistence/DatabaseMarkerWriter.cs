using Microsoft.EntityFrameworkCore;
using Zentory.Domain.Entities;

namespace Zentory.Infrastructure.Persistence;

// Escritura de la marca de base local (PLAN-0018 RF-63 h, RF-4; SR-0018.33; D-42). La llaman db init (sobre una base
// vacía y después de un --reset) y el arranque en Development cuando DatabaseStartupPolicy devuelve WriteLocalMarker.
// Nunca una migración.
public static class DatabaseMarkerWriter
{
    // Idempotente: inserta solo si la tabla está vacía. Devuelve true si insertó la fila.
    public static async Task<bool> EnsureLocalMarkerAsync(
        ZentoryDbContext db, DateTime createdAtUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.DatabaseMarkers.AnyAsync(ct)) return false;

        db.DatabaseMarkers.Add(DatabaseMarker.CreateLocal(createdAtUtc));
        await db.SaveChangesAsync(ct);
        return true;
    }
}

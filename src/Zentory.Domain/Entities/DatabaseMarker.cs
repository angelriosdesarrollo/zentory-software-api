namespace Zentory.Domain.Entities;

// Marca de base local (PLAN-0018 RF-63 h, RF-4; SR-0018.33; D-42). Una fila en core.database_marker dice que la base la
// creó db init o el arranque en Development contra un host loopback, y es lo que db init --reset exige antes de
// destruir datos: una base remota vista por un túnel o creada por el pipeline nunca la tiene, porque ninguna migración
// la escribe. Global: no es de tenant, no tiene OrganizationId ni filtro.
public class DatabaseMarker
{
    // Único valor que se escribe hoy. La columna admite otros para que un ambiente futuro se distinga sin migración.
    public const string LocalEnvironment = "local";

    public Guid     Id          { get; private set; } = Guid.NewGuid();
    public string   Environment { get; private set; } = default!;
    public DateTime CreatedAt   { get; private set; }

    private DatabaseMarker() { }

    public static DatabaseMarker CreateLocal(DateTime createdAtUtc) => new()
    {
        Environment = LocalEnvironment,
        CreatedAt   = createdAtUtc,
    };

    public bool IsLocal => string.Equals(Environment, LocalEnvironment, StringComparison.Ordinal);
}

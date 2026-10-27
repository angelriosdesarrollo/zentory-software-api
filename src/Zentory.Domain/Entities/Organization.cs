namespace Zentory.Domain.Entities;

public class Organization
{
    public Guid   OrganizationId { get; private set; } = Guid.NewGuid();
    public Guid?  OwnerId        { get; private set; }
    public string Name           { get; private set; } = default!;
    public string LegalType    { get; private set; } = default!;
    public string Country        { get; private set; } = "CO";
    public bool   IsActive       { get; private set; } = true;

    // Borrado lógico (PLAN-0018 RF-65 b; ADR purga-organizaciones-archivo-legal §1). La fila y todos sus datos siguen
    // en la base hasta la purga; el filtro global DeletedAt == null la oculta a toda lectura que no esté en la lista
    // blanca de IgnoreQueryFilters.
    public DateTime? DeletedAt      { get; private set; }
    public Guid?     DeletedBy      { get; private set; }
    public DateTime? PurgeStartedAt { get; private set; }

    // Token de concurrencia (RF-65 b2, SR-0018.37; D-45): toda operación que cambia el owner o el estado lo regenera,
    // así quien escribe con un sello leído antes falla con DbUpdateConcurrencyException en lugar de pisar el cambio.
    public Guid ConcurrencyStamp { get; private set; } = Guid.NewGuid();

    private Organization() { }

    public static Organization Create(string name, string legalType, string country = "CO", Guid? id = null)
    {
        return new Organization
        {
            OrganizationId = id ?? Guid.NewGuid(),
            Name           = name,
            LegalType    = legalType,
            Country        = country
        };
    }

    public void SetOwner(Guid userId)
    {
        OwnerId = userId;
        TouchConcurrencyStamp();
    }

    public void MarkDeleted(Guid deletedBy, DateTime deletedAtUtc)
    {
        DeletedAt = deletedAtUtc;
        DeletedBy = deletedBy;
        TouchConcurrencyStamp();
    }

    // Restauración dentro del plazo (RF-72 b): vuelve tal como estaba. Regenera el sello para que la purga que leyó la
    // organización antes no la marque después (RF-78 1).
    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
        TouchConcurrencyStamp();
    }

    // Inicio de la purga física (RF-78 1, ADR purga §4.1). Se confirma antes de borrar nada y con el sello leído como token:
    // si una restauración ganó, el guardado falla por concurrencia y la organización se salta. Desde aquí restore
    // responde 404 (RF-72 d).
    public void StartPurge(DateTime startedAtUtc)
    {
        if (DeletedAt is null)
            throw new InvalidOperationException("Solo se purga una organización eliminada.");
        PurgeStartedAt = startedAtUtc;
        TouchConcurrencyStamp();
    }

    // Público para las escrituras que no cambian la fila pero dependen de su owner (PLAN-0018 RF-83 e, RF-87 b; T30c):
    // cambiar el rol o dar de baja a un miembro se condiciona al sello leído, así una transferencia aceptada entre medias
    // (que cambia OwnerId) las hace fallar en lugar de degradar o borrar al nuevo owner.
    public void TouchConcurrencyStamp() => ConcurrencyStamp = Guid.NewGuid();
}

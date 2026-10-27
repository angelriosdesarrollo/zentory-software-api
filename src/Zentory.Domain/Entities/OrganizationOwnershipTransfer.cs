using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Estados de una propuesta de transferencia de la propiedad (PLAN-0018 RF-64, RF-83; D-33; migración M4c).
public static class OwnershipTransferStatus
{
    public const string Pending   = "pending";
    public const string Accepted  = "accepted";
    public const string Declined  = "declined";
    public const string Cancelled = "cancelled";

    public static IReadOnlyList<string> All { get; } = [Pending, Accepted, Declined, Cancelled];
}

// Propuesta de transferencia de la propiedad de una organización (PLAN-0018 RF-64 c, c2, c3; RF-83; D-33; C-12;
// migración M4c). Proponer no cambia nada de la organización: solo deja esta fila pending con el previewHash que el owner
// acusó. Status es token de concurrencia (OrganizationOwnershipTransferConfiguration): aceptar, rechazar y cancelar a la
// vez dejan un solo ganador. Hay como máximo una pending por organización (índice único filtrado); una vencida se trata
// como inexistente y pasa a cancelled al detectarse, sin job.
public class OrganizationOwnershipTransfer : TenantEntity
{
    public const int PreviewHashLength = 64;

    public Guid      FromUserId  { get; private set; }
    public Guid      ToUserId    { get; private set; }
    public string    PreviewHash { get; private set; } = default!;  // SHA-256 hex del aviso previo (RF-77)
    public string    Status      { get; private set; } = default!;  // Un valor de OwnershipTransferStatus
    public DateTime  ExpiresAt   { get; private set; }
    public DateTime? ResolvedAt  { get; private set; }

    private OrganizationOwnershipTransfer() { }

    // Propuesta nueva con la vigencia contada desde nowUtc. Las reglas (destino válido, límite, aviso acusado, una sola
    // pendiente) las decide el comando antes de llamarla.
    public static OrganizationOwnershipTransfer Propose(
        Guid     organizationId,
        Guid     fromUserId,
        Guid     toUserId,
        string   previewHash,
        DateTime nowUtc,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewHash);
        if (fromUserId == toUserId)
            throw new ArgumentException("El destino de la transferencia no puede ser quien la propone.", nameof(toUserId));
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        return new OrganizationOwnershipTransfer
        {
            OrganizationId = organizationId,
            FromUserId     = fromUserId,
            ToUserId       = toUserId,
            PreviewHash    = previewHash,
            Status         = OwnershipTransferStatus.Pending,
            ExpiresAt      = nowUtc.Add(lifetime),
            CreatedAt      = nowUtc,
            UpdatedAt      = nowUtc,
        };
    }

    public bool IsPending => Status == OwnershipTransferStatus.Pending;

    // Vigente: pending y sin vencer (RF-64 c2). Una pending vencida no bloquea una propuesta nueva.
    public bool IsPendingAt(DateTime nowUtc) => IsPending && nowUtc < ExpiresAt;

    // Cancela una pending, vigente o vencida (RF-64 c2, c3; RF-83 h). Sobre una ya resuelta no hace nada: cancelar es
    // idempotente. Devuelve true si cambió el estado.
    public bool Cancel(DateTime nowUtc) => Resolve(OwnershipTransferStatus.Cancelled, nowUtc);

    // El destino acepta (RF-83 b, e) o rechaza (RF-83 f) una pending vigente. Las reglas (destino, vigencia, credencial,
    // revalidación) las decide el comando; aquí solo se exige que siga pending. Devuelve true si cambió el estado.
    public bool Accept(DateTime nowUtc) => Resolve(OwnershipTransferStatus.Accepted, nowUtc);

    public bool Decline(DateTime nowUtc) => Resolve(OwnershipTransferStatus.Declined, nowUtc);

    private bool Resolve(string status, DateTime nowUtc)
    {
        if (!IsPending)
            return false;
        Status     = status;
        ResolvedAt = nowUtc;
        UpdatedAt  = nowUtc;
        return true;
    }
}

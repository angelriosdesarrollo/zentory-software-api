using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Historial de cada intento de subida de un PilaVerification. PilaVerification.DocumentUrl
// siempre apunta al más reciente (comportamiento preexistente, sin romper el flujo de
// descarga actual); esta tabla preserva los intentos anteriores — relevante cuando la
// empresa rechaza un comprobante y el colaborador vuelve a subir con el mismo link, para
// no perder la evidencia que originó el rechazo.
public class PilaVerificationDocument : BaseEntity
{
    public Guid     PilaVerificationId { get; private set; }
    public string   StorageKey         { get; private set; } = default!;
    public string?  FileName           { get; private set; }
    public long?    FileSize           { get; private set; }
    public string?  ContentType        { get; private set; }
    public DateTime UploadedAt         { get; private set; } = DateTime.UtcNow;

    private PilaVerificationDocument() { }

    public static PilaVerificationDocument Create(
        Guid    pilaVerificationId,
        string  storageKey,
        string? fileName    = null,
        long?   fileSize    = null,
        string? contentType = null)
    {
        return new PilaVerificationDocument
        {
            PilaVerificationId = pilaVerificationId,
            StorageKey         = storageKey,
            FileName           = fileName,
            FileSize           = fileSize,
            ContentType        = contentType,
        };
    }
}

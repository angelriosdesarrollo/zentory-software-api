using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class CollaboratorPayoutInvoice : TenantEntity
{
    public Guid     CollaboratorId  { get; private set; }
    public string   Period          { get; private set; } = default!;  // 'YYYY-MM'
    public string   Concept         { get; private set; } = default!;
    public decimal  Amount          { get; private set; }
    public string   Currency        { get; private set; } = "COP";
    public string   Status          { get; private set; } = "draft";
    // 'draft' | 'generated' | 'sent' | 'signed' | 'uploaded_manually' | 'approved' | 'rejected' | 'disputed'
    // 'signed' solo aplica a Source == "generated": el colaborador descargó el borrador
    // generado por la empresa, lo firmó fuera de la plataforma y subió la versión firmada,
    // que reemplaza el documento generado (no se conservan ambos).
    // 'approved'/'rejected': la empresa revisó el documento ya subido/firmado (mismo patrón
    // que PilaVerification.Status 'verificada'/'rechazada'). Un rechazo exige Notes — el
    // colaborador lo ve en su portal y puede volver a subir, lo que reemplaza el documento
    // y saca a la fila de 'rejected' (vía MarkSigned/MarkUploadedManually otra vez).
    // 'disputed': dirección opuesta a 'rejected' — el colaborador rechaza una cuenta de
    // cobro generada por la empresa (Source == "generated") en vez de firmarla. Exige Notes
    // igual que 'rejected'. No hay acción de "resolver" automática — la empresa ve el motivo
    // y corrige generando una cuenta de cobro nueva (ya es un caso soportado, no exclusivo).
    public string?  Notes           { get; private set; }
    public string   Source          { get; private set; } = "generated";
    // 'generated' (la empresa generó el PDF) | 'manual_upload' (la empresa le pidió al
    // colaborador que suba la suya) | 'self_service' (el colaborador la subió por su cuenta
    // desde el portal, sin que nadie se lo pidiera)
    public string?  StorageKey      { get; private set; }
    public string?  DocumentFileName    { get; private set; }
    public long?    DocumentFileSize    { get; private set; }
    public string?  DocumentContentType { get; private set; }
    public decimal? DeclaredAmount  { get; private set; }  // monto que el colaborador declaró al subir (source=manual_upload)
    public string?  SignedByName    { get; private set; }  // firma electrónica: nombre escrito por el colaborador
    public DateTime? SignedAt       { get; private set; }
    public Guid     PublicToken     { get; private set; } = Guid.NewGuid();
    public DateTime? TokenExpiresAt { get; private set; }
    public DateTime? GeneratedAt    { get; private set; }
    public DateTime? SentAt         { get; private set; }
    public Guid?    CreatedBy       { get; private set; }

    public DateTime? DeletedAt      { get; private set; }

    private CollaboratorPayoutInvoice() { }

    public static CollaboratorPayoutInvoice Create(
        Guid      organizationId,
        Guid      collaboratorId,
        string    period,
        string    concept,
        decimal   amount,
        string    currency      = "COP",
        string    source        = "generated",
        Guid?     createdBy     = null,
        TimeSpan? tokenValidity = null)
    {
        return new CollaboratorPayoutInvoice
        {
            OrganizationId = organizationId,
            CollaboratorId = collaboratorId,
            Period         = period,
            Concept        = concept,
            Amount         = amount,
            Currency       = currency,
            Source         = source,
            CreatedBy      = createdBy,
            TokenExpiresAt = DateTime.UtcNow.Add(tokenValidity ?? TimeSpan.FromDays(30)),
        };
    }

    public void MarkGenerated(string storageKey, string? fileName = null, long? fileSize = null, string? contentType = null)
    {
        StorageKey          = storageKey;
        DocumentFileName    = fileName;
        DocumentFileSize    = fileSize;
        DocumentContentType = contentType;
        Status              = "generated";
        GeneratedAt         = DateTime.UtcNow;
        UpdatedAt           = DateTime.UtcNow;
    }

    public void MarkSent()
    {
        Status    = "sent";
        SentAt    = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkSigned(
        string storageKey, string? fileName = null, long? fileSize = null, string? contentType = null,
        string? signedByName = null)
    {
        StorageKey          = storageKey;
        DocumentFileName     = fileName;
        DocumentFileSize     = fileSize;
        DocumentContentType  = contentType;
        Status               = "signed";
        // signedByName solo se setea en la firma electrónica (SignOwnPayoutInvoiceCommand);
        // si el colaborador sube un archivo ya firmado por fuera, no hay nombre que registrar acá.
        if (signedByName is not null)
        {
            SignedByName = signedByName;
            SignedAt     = DateTime.UtcNow;
        }
        UpdatedAt            = DateTime.UtcNow;
    }

    public void MarkUploadedManually(
        string storageKey, decimal declaredAmount, string? fileName = null, long? fileSize = null, string? contentType = null)
    {
        StorageKey          = storageKey;
        DeclaredAmount       = declaredAmount;
        DocumentFileName     = fileName;
        DocumentFileSize     = fileSize;
        DocumentContentType  = contentType;
        Status               = "uploaded_manually";
        UpdatedAt            = DateTime.UtcNow;
    }

    public void Approve()
    {
        Status    = "approved";
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string notes)
    {
        Status    = "rejected";
        Notes     = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Dispute(string notes)
    {
        Status    = "disputed";
        Notes     = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RegenerateToken(TimeSpan? tokenValidity = null)
    {
        PublicToken    = Guid.NewGuid();
        TokenExpiresAt = DateTime.UtcNow.Add(tokenValidity ?? TimeSpan.FromDays(30));
        UpdatedAt      = DateTime.UtcNow;
    }

    public bool IsTokenValid() => TokenExpiresAt is null || TokenExpiresAt > DateTime.UtcNow;

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

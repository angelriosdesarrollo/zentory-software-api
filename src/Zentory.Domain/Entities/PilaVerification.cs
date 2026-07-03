using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class PilaVerification : BaseEntity
{
    public Guid     OrganizationId  { get; private set; }
    public Guid     CollaboratorId  { get; private set; }
    public string   Period          { get; private set; } = default!;  // 'YYYY-MM'
    public string   Status          { get; private set; } = "solicitada";
    // 'solicitada' | 'recibida' | 'verificada' | 'rechazada'
    public string   Source          { get; private set; } = "company_requested";
    // 'company_requested' (la empresa la solicitó) | 'self_service' (el colaborador la subió
    // por su cuenta desde el portal, sin que nadie se lo pidiera)
    public DateTime RequestedAt     { get; private set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt     { get; private set; }
    public DateTime? VerifiedAt     { get; private set; }
    public string?  DocumentUrl     { get; private set; }  // R2 object key del comprobante recibido (el más reciente)
    public string?  DocumentFileName    { get; private set; }
    public long?    DocumentFileSize    { get; private set; }
    public string?  DocumentContentType { get; private set; }
    public string?  Notes           { get; private set; }
    public Guid?    CreatedBy       { get; private set; }

    // Token de acceso público (flujo de escritura, a diferencia de Proposal.PublicToken de solo lectura)
    public Guid      Token          { get; private set; } = Guid.NewGuid();
    public DateTime? TokenExpiresAt { get; private set; }

    private PilaVerification() { }

    public static PilaVerification Create(
        Guid     organizationId,
        Guid     collaboratorId,
        string   period,
        Guid?    createdBy = null,
        TimeSpan? tokenValidity = null,
        string   source = "company_requested")
    {
        return new PilaVerification
        {
            OrganizationId = organizationId,
            CollaboratorId = collaboratorId,
            Period         = period,
            CreatedBy      = createdBy,
            Source         = source,
            TokenExpiresAt = DateTime.UtcNow.Add(tokenValidity ?? TimeSpan.FromDays(30))
        };
    }

    public void RegenerateToken(TimeSpan? tokenValidity = null)
    {
        Token          = Guid.NewGuid();
        TokenExpiresAt = DateTime.UtcNow.Add(tokenValidity ?? TimeSpan.FromDays(30));
        RequestedAt    = DateTime.UtcNow;
    }

    public bool IsTokenValid() => TokenExpiresAt is null || TokenExpiresAt > DateTime.UtcNow;

    public void MarkReceived(
        string? documentUrl,
        string? fileName    = null,
        long?   fileSize    = null,
        string? contentType = null)
    {
        Status              = "recibida";
        ReceivedAt          = DateTime.UtcNow;
        DocumentUrl         = documentUrl;
        DocumentFileName    = fileName;
        DocumentFileSize    = fileSize;
        DocumentContentType = contentType;
    }

    public void MarkVerified()
    {
        Status     = "verificada";
        VerifiedAt = DateTime.UtcNow;
    }

    public void Reject(string? notes = null)
    {
        Status = "rechazada";
        Notes  = notes;
    }
}

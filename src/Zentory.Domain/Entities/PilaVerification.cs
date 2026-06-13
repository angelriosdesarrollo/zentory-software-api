using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class PilaVerification : BaseEntity
{
    public Guid     OrganizationId  { get; private set; }
    public Guid     CollaboratorId  { get; private set; }
    public string   Period          { get; private set; } = default!;  // 'YYYY-MM'
    public string   Status          { get; private set; } = "solicitada";
    // 'solicitada' | 'recibida' | 'verificada' | 'rechazada'
    public DateTime RequestedAt     { get; private set; } = DateTime.UtcNow;
    public DateTime? ReceivedAt     { get; private set; }
    public DateTime? VerifiedAt     { get; private set; }
    public string?  DocumentUrl     { get; private set; }
    public string?  Notes           { get; private set; }
    public Guid?    CreatedBy       { get; private set; }

    private PilaVerification() { }

    public static PilaVerification Create(
        Guid   organizationId,
        Guid   collaboratorId,
        string period,
        Guid?  createdBy = null)
    {
        return new PilaVerification
        {
            OrganizationId = organizationId,
            CollaboratorId = collaboratorId,
            Period         = period,
            CreatedBy      = createdBy
        };
    }

    public void MarkReceived(string? documentUrl = null)
    {
        Status      = "recibida";
        ReceivedAt  = DateTime.UtcNow;
        DocumentUrl = documentUrl;
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

using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class SystemEvent : BaseEntity
{
    public Guid     OrganizationId { get; private set; }
    public string   Type           { get; private set; } = default!;
    // 'proposal.accepted' | 'invoice.overdue' | 'trial.ending' | etc.
    public string   Payload        { get; private set; } = default!;  // JSONB as string
    public bool     Processed      { get; private set; }
    public DateTime? ProcessedAt   { get; private set; }
    public string?  ErrorMessage   { get; private set; }
    public short    RetryCount     { get; private set; }

    private SystemEvent() { }

    public static SystemEvent Create(Guid organizationId, string type, string payloadJson)
    {
        return new SystemEvent
        {
            OrganizationId = organizationId,
            Type           = type,
            Payload        = payloadJson
        };
    }

    public void MarkProcessed()
    {
        Processed    = true;
        ProcessedAt  = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage)
    {
        RetryCount++;
        ErrorMessage = errorMessage;
    }
}

using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

// Partitioned by created_at (annual). Insert-only — never update or delete.
public class AuditLog : BaseEntity
{
    public Guid?    OrganizationId { get; private set; }
    public Guid?    UserId         { get; private set; }
    public string   TableName      { get; private set; } = default!;
    public Guid     RecordId       { get; private set; }
    public string   Action         { get; private set; } = default!;  // 'INSERT' | 'UPDATE' | 'DELETE'
    public string?  OldValues      { get; private set; }  // JSONB as string
    public string?  NewValues      { get; private set; }  // JSONB as string
    public string?  IpAddress      { get; private set; }
    public string?  UserAgent      { get; private set; }

    private AuditLog() { }

    public static AuditLog Record(
        string  tableName,
        Guid    recordId,
        string  action,
        Guid?   organizationId = null,
        Guid?   userId         = null,
        string? oldValues      = null,
        string? newValues      = null,
        string? ipAddress      = null,
        string? userAgent      = null)
    {
        return new AuditLog
        {
            OrganizationId = organizationId,
            UserId         = userId,
            TableName      = tableName,
            RecordId       = recordId,
            Action         = action,
            OldValues      = oldValues,
            NewValues      = newValues,
            IpAddress      = ipAddress,
            UserAgent      = userAgent
        };
    }
}

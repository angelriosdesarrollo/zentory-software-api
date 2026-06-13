using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid     OrganizationId { get; private set; }
    public Guid     UserId         { get; private set; }
    public string   Type           { get; private set; } = default!;
    // 'proposal_accepted' | 'invoice_overdue' | 'payment_received' | 'trial_ending'
    public string   Title          { get; private set; } = default!;
    public string?  Body           { get; private set; }
    public string?  ActionUrl      { get; private set; }
    public DateTime? ReadAt        { get; private set; }

    private Notification() { }

    public static Notification Create(
        Guid    organizationId,
        Guid    userId,
        string  type,
        string  title,
        string? body      = null,
        string? actionUrl = null)
    {
        return new Notification
        {
            OrganizationId = organizationId,
            UserId         = userId,
            Type           = type,
            Title          = title,
            Body           = body,
            ActionUrl      = actionUrl
        };
    }

    public void MarkRead() { ReadAt = DateTime.UtcNow; }
}

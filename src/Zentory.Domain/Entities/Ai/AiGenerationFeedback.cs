using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

public class AiGenerationFeedback : BaseEntity
{
    public Guid     UsageLogId      { get; private set; }
    public Guid     OrganizationId  { get; private set; }
    public Guid?    UserId          { get; private set; }
    public string   Action          { get; private set; } = default!;
    // 'accepted' | 'edited' | 'rejected'
    public decimal? EditDistancePct { get; private set; }  // 0-100
    public short?   Rating          { get; private set; }  // 1-5
    public string?  Comment         { get; private set; }

    private AiGenerationFeedback() { }

    public static AiGenerationFeedback Create(
        Guid    usageLogId,
        Guid    organizationId,
        string  action,
        Guid?   userId          = null,
        decimal? editDistancePct= null,
        short?  rating          = null,
        string? comment         = null)
    {
        return new AiGenerationFeedback
        {
            UsageLogId      = usageLogId,
            OrganizationId  = organizationId,
            Action          = action,
            UserId          = userId,
            EditDistancePct = editDistancePct,
            Rating          = rating,
            Comment         = comment
        };
    }
}

using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities.Ai;

public class AiFeature : BaseEntity
{
    public string   Key         { get; private set; } = default!;  // 'proposal_section_enrich' | etc.
    public string   DisplayName { get; private set; } = default!;
    public string?  Description { get; private set; }
    public string   Category    { get; private set; } = default!;
    // 'text_generation' | 'analysis' | 'prediction' | 'summarization'
    public bool     Active      { get; private set; } = true;

    private AiFeature() { }

    public static AiFeature Create(string key, string displayName, string category, string? description = null)
    {
        return new AiFeature { Key = key, DisplayName = displayName, Category = category, Description = description };
    }

    public void Deactivate() { Active = false; }
}

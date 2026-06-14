using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class ProposalSection : BaseEntity
{
    public Guid    OrganizationId { get; private set; }
    public Guid    ProposalId     { get; private set; }
    public string  SectionType    { get; private set; } = default!;
    // 'overview'|'scope'|'deliverables'|'timeline'|'team'
    // 'pricing'|'conditions'|'about_us'|'acceptance'|'custom'
    public string? Title          { get; private set; }
    public string? Content        { get; private set; }  // markdown
    public short   SortOrder      { get; private set; }
    public bool    IsVisible      { get; private set; } = true;
    public bool    IsEncrypted    { get; private set; }

    private ProposalSection() { }

    public static ProposalSection Create(
        Guid    organizationId,
        Guid    proposalId,
        string  sectionType,
        short   sortOrder    = 0,
        string? title        = null,
        string? content      = null,
        bool    isVisible    = true,
        bool    isEncrypted  = false)
    {
        return new ProposalSection
        {
            OrganizationId = organizationId,
            ProposalId     = proposalId,
            SectionType    = sectionType,
            Title          = title,
            Content        = content,
            SortOrder      = sortOrder,
            IsVisible      = isVisible,
            IsEncrypted    = isEncrypted,
        };
    }

    public void Update(string? title, string? content, bool isVisible, short sortOrder, bool isEncrypted)
    {
        Title       = title;
        Content     = content;
        IsVisible   = isVisible;
        SortOrder   = sortOrder;
        IsEncrypted = isEncrypted;
        Touch();
    }
}

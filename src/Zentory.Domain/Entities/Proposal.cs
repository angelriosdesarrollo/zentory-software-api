using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class Proposal : TenantEntity
{
    public Guid     ClientId                { get; private set; }
    public Guid?    TemplateId              { get; private set; }

    public string   Title                   { get; private set; } = default!;
    public string   Status                  { get; private set; } = "draft";
    // 'draft' | 'sent' | 'viewed' | 'accepted' | 'rejected' | 'expired'

    public Guid     PublicToken             { get; private set; } = Guid.NewGuid();
    public DateTime? PublicTokenExpiresAt   { get; private set; }

    public decimal? TotalAmount             { get; private set; }
    public string   Currency                { get; private set; } = "COP";
    public string?  IntroText               { get; private set; }
    public string?  Conditions              { get; private set; }  // JSONB as string

    // Tracking de apertura (Pro)
    public DateTime? SentAt                 { get; private set; }
    public DateTime? FirstViewedAt          { get; private set; }
    public DateTime? LastViewedAt           { get; private set; }
    public int       ViewCount              { get; private set; }
    public DateTime? AcceptedAt             { get; private set; }
    public DateTime? RejectedAt             { get; private set; }
    public DateTime? ExpiresAt              { get; private set; }

    public Guid?    ConvertedToProjectId    { get; private set; }
    public DateTime? ConvertedAt            { get; private set; }

    public Guid?    CreatedBy               { get; private set; }

    public DateTime? DeletedAt              { get; private set; }

    private readonly List<ProposalSection> _sections = new();
    private readonly List<ProposalItem>    _items    = new();

    public IReadOnlyList<ProposalSection> Sections => _sections.AsReadOnly();
    public IReadOnlyList<ProposalItem>    Items    => _items.AsReadOnly();

    private Proposal() { }

    public static Proposal Create(
        Guid    organizationId,
        Guid    clientId,
        string  title,
        string  currency    = "COP",
        Guid?   templateId  = null,
        Guid?   createdBy   = null,
        DateTime? expiresAt = null)
    {
        return new Proposal
        {
            OrganizationId = organizationId,
            ClientId       = clientId,
            Title          = title,
            Currency       = currency,
            TemplateId     = templateId,
            CreatedBy      = createdBy,
            ExpiresAt      = expiresAt
        };
    }

    public void UpdateContent(string title, string? introText, string? conditions)
    {
        Title      = title;
        IntroText  = introText;
        Conditions = conditions;
        UpdatedAt  = DateTime.UtcNow;
    }

    public void AddItem(ProposalItem item) { _items.Add(item); RecalculateTotal(); }

    public void RecalculateTotal()
    {
        TotalAmount = _items.Sum(i => i.Total);
        UpdatedAt   = DateTime.UtcNow;
    }

    public void MarkAsSent()
    {
        Status    = "sent";
        SentAt    = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordView()
    {
        ViewCount++;
        LastViewedAt  = DateTime.UtcNow;
        FirstViewedAt ??= DateTime.UtcNow;
        if (Status == "sent") Status = "viewed";
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsAccepted()
    {
        Status      = "accepted";
        AcceptedAt  = DateTime.UtcNow;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void MarkAsRejected()
    {
        Status      = "rejected";
        RejectedAt  = DateTime.UtcNow;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void ConvertToProject(Guid projectId)
    {
        ConvertedToProjectId = projectId;
        ConvertedAt          = DateTime.UtcNow;
        UpdatedAt            = DateTime.UtcNow;
    }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

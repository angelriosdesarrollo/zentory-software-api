using Zentory.Domain.Entities.Common;

namespace Zentory.Domain.Entities;

public class Invoice : TenantEntity
{
    public Guid     ClientId            { get; private set; }
    public Guid?    ProjectId           { get; private set; }

    public string   DocumentType        { get; private set; } = "cobro";
    // 'cobro' (freelance) | 'factura' (empresa) | 'nota_credito'
    public string   InvoiceNumber       { get; private set; } = default!;
    public string   Status              { get; private set; } = "draft";
    // 'draft' | 'sent' | 'viewed' | 'paid' | 'partial' | 'overdue' | 'cancelled'

    public DateOnly IssuedAt            { get; private set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly DueAt               { get; private set; }

    public decimal  Subtotal            { get; private set; }
    public decimal  TaxAmount           { get; private set; }
    public decimal  Total               { get; private set; }
    public decimal  AmountPaid          { get; private set; }
    public decimal  AmountDue           { get; private set; }
    public string   Currency            { get; private set; } = "COP";

    public decimal? ExchangeRate        { get; private set; }
    public decimal? TotalBaseCurrency   { get; private set; }

    // DIAN (empresa + Colombia + pro)
    public string?  DianCufe            { get; private set; }
    public string?  DianStatus          { get; private set; }
    public DateTime? DianSubmittedAt    { get; private set; }
    public string?  DianResponse        { get; private set; }  // JSONB as string

    public string?  Notes               { get; private set; }
    public string?  PaymentTerms        { get; private set; }
    public string?  PaymentInstructions { get; private set; }

    public DateTime? SentAt             { get; private set; }
    public DateTime? FirstViewedAt      { get; private set; }
    public DateTime? LastViewedAt       { get; private set; }
    public int       ViewCount          { get; private set; }

    public DateTime? DeletedAt          { get; private set; }

    private readonly List<InvoiceItem> _items = new();
    public IReadOnlyList<InvoiceItem> Items => _items.AsReadOnly();

    private readonly List<InvoicePayment> _payments = new();
    public IReadOnlyList<InvoicePayment> Payments => _payments.AsReadOnly();

    private Invoice() { }

    public static Invoice Create(
        Guid     organizationId,
        Guid     clientId,
        string   invoiceNumber,
        DateOnly dueAt,
        string   currency      = "COP",
        string   documentType  = "cobro",
        Guid?    projectId     = null,
        string?  notes         = null,
        string?  paymentTerms  = null)
    {
        return new Invoice
        {
            OrganizationId = organizationId,
            ClientId       = clientId,
            InvoiceNumber  = invoiceNumber,
            DueAt          = dueAt,
            Currency       = currency,
            DocumentType   = documentType,
            ProjectId      = projectId,
            Notes          = notes,
            PaymentTerms   = paymentTerms,
            AmountDue      = 0
        };
    }

    public void AddItem(InvoiceItem item) { _items.Add(item); RecalculateTotals(); }

    public void RecalculateTotals()
    {
        Subtotal   = _items.Sum(i => i.Total);
        Total      = Subtotal + TaxAmount;
        AmountDue  = Total - AmountPaid;
        UpdatedAt  = DateTime.UtcNow;
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
        LastViewedAt ??= DateTime.UtcNow;
        FirstViewedAt = FirstViewedAt ?? DateTime.UtcNow;
        if (Status == "sent") Status = "viewed";
        UpdatedAt = DateTime.UtcNow;
    }

    public void RecordPayment(decimal amount)
    {
        AmountPaid += amount;
        AmountDue   = Total - AmountPaid;
        Status      = AmountDue <= 0 ? "paid" : AmountPaid > 0 ? "partial" : Status;
        UpdatedAt   = DateTime.UtcNow;
    }

    public void MarkAsOverdue()   { Status = "overdue";   UpdatedAt = DateTime.UtcNow; }
    public void MarkAsCancelled() { Status = "cancelled"; UpdatedAt = DateTime.UtcNow; }

    public void SoftDelete() { DeletedAt = DateTime.UtcNow; UpdatedAt = DateTime.UtcNow; }
}

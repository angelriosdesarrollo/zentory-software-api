namespace Zentory.Domain.Entities;

// Composite PK: (OrganizationId, DocumentType, Year).
// Controlled by InvoiceRepository.NextNumberAsync — never update directly.
public class OrganizationInvoiceSequence
{
    public Guid    OrganizationId { get; private set; }
    public string  DocumentType   { get; private set; } = default!;
    // 'factura' | 'cobro' | 'nota_credito'
    public short   Year           { get; private set; }
    public string  Prefix         { get; private set; } = default!;  // 'FV' | 'CC' | 'NC'
    public int     LastNumber     { get; private set; }

    private OrganizationInvoiceSequence() { }

    public static OrganizationInvoiceSequence Initialize(
        Guid   organizationId,
        string documentType,
        short  year,
        string prefix)
    {
        return new OrganizationInvoiceSequence
        {
            OrganizationId = organizationId,
            DocumentType   = documentType,
            Year           = year,
            Prefix         = prefix,
            LastNumber     = 0
        };
    }

    public int Increment()
    {
        LastNumber++;
        return LastNumber;
    }
}

namespace Zentory.Domain.Entities;

// Archivo legal de documentos emitidos (PLAN-0018 RF-79, ADR purga §5; retención DIAN). La purga de una organización
// copia aquí sus facturas y cuentas de cobro emitidas antes de borrar sus filas. A propósito no es TenantEntity, no
// guarda OrganizationId ni tiene FK a Organization: así la purga puede ser completa y el archivo no depende del filtro de
// tenant. Ningún endpoint ni DTO la expone (acceso del operador por base, SR-0018.28).
public class LegalDocumentArchive
{
    // Prefijo de las claves de PDF archivados: legal-archive/{Guid aleatorio}.pdf, sin número, NIT ni organización
    // (SR-0018.28 a).
    public const string PdfKeyPrefix = "legal-archive/";

    public Guid     Id              { get; private set; } = Guid.NewGuid();
    public string   SourceType      { get; private set; } = default!;
    public Guid     SourceId        { get; private set; }  // Id del documento original; único con SourceType
    public string   DocumentNumber  { get; private set; } = default!;
    public DateTime IssuedAt        { get; private set; }
    public string   IssuerTaxId     { get; private set; } = default!;  // tal como figura en el documento
    public string   IssuerLegalName { get; private set; } = default!;  // tal como figura en el documento
    public string?  DianCufe        { get; private set; }
    public string   Snapshot        { get; private set; } = default!;  // JSONB as string
    public string?  PdfStorageKey   { get; private set; }
    public DateTime ArchivedAt      { get; private set; }
    public DateTime RetainUntil     { get; private set; }

    private LegalDocumentArchive() { }

    public static LegalDocumentArchive Create(
        string   sourceType,
        Guid     sourceId,
        string   documentNumber,
        DateTime issuedAt,
        string   issuerTaxId,
        string   issuerLegalName,
        string?  dianCufe,
        string   snapshotJson,
        string?  pdfStorageKey,
        DateTime archivedAt,
        int      retentionYears)
    {
        if (!LegalDocumentSourceType.IsKnown(sourceType))
            throw new ArgumentException($"Tipo de documento legal desconocido: {sourceType}.", nameof(sourceType));
        if (sourceId == Guid.Empty)
            throw new ArgumentException("El documento archivado necesita el Id de su origen.", nameof(sourceId));
        if (retentionYears < 1)
            throw new ArgumentOutOfRangeException(nameof(retentionYears), "La retención legal es de al menos un año.");
        if (pdfStorageKey is not null && !pdfStorageKey.StartsWith(PdfKeyPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"El PDF archivado debe vivir bajo {PdfKeyPrefix}.", nameof(pdfStorageKey));

        return new LegalDocumentArchive
        {
            SourceType      = sourceType,
            SourceId        = sourceId,
            DocumentNumber  = documentNumber,
            IssuedAt        = issuedAt,
            IssuerTaxId     = issuerTaxId,
            IssuerLegalName = issuerLegalName,
            DianCufe        = dianCufe,
            Snapshot        = snapshotJson,
            PdfStorageKey   = pdfStorageKey,
            ArchivedAt      = archivedAt,
            RetainUntil     = issuedAt.AddYears(retentionYears),
        };
    }

    // Clave nueva para el PDF archivado: aleatoria, para que no revele número, NIT ni organización (SR-0018.28 a).
    public static string NewPdfStorageKey() => $"{PdfKeyPrefix}{Guid.NewGuid()}.pdf";

    public bool IsExpired(DateTime nowUtc) => RetainUntil < nowUtc;
}

// Origen de un documento archivado (RF-79): facturas emitidas y cuentas de cobro emitidas.
public static class LegalDocumentSourceType
{
    public const string Invoice                   = "invoice";
    public const string CollaboratorPayoutInvoice = "collaborator_payout_invoice";

    public static bool IsKnown(string? value) => value is Invoice or CollaboratorPayoutInvoice;
}

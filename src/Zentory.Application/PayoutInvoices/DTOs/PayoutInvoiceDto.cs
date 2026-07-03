namespace Zentory.Application.PayoutInvoices.DTOs;

public record PayoutInvoiceDto(
    Guid      Id,
    Guid      CollaboratorId,
    string    Period,
    string    Concept,
    decimal   Amount,
    decimal?  DeclaredAmount,  // monto que el colaborador declaró al subir (solo source=manual_upload)
    string    Currency,
    string    Status,   // 'draft' | 'generated' | 'sent' | 'uploaded_manually'
    string    Source,   // 'generated' | 'manual_upload'
    string?   DocumentFileName,
    long?     DocumentFileSize,
    DateTime? GeneratedAt,
    DateTime? SentAt,
    DateTime  CreatedAt,
    DateTime? RetentionUntil);

public record SuggestedPayoutAmountDto(
    decimal Amount,
    decimal Hours,
    bool    HasTimeEntries);

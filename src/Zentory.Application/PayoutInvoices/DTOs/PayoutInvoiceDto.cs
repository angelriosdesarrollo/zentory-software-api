namespace Zentory.Application.PayoutInvoices.DTOs;

public record PayoutInvoiceDto(
    Guid      Id,
    Guid      CollaboratorId,
    string    Period,
    string    Concept,
    decimal   Amount,
    decimal?  DeclaredAmount,  // monto que el colaborador declaró al subir (solo source=manual_upload)
    string    Currency,
    string    Status,   // 'draft' | 'generated' | 'sent' | 'signed' | 'uploaded_manually' | 'approved' | 'rejected'
    string    Source,   // 'generated' | 'manual_upload' | 'self_service'
    string?   DocumentFileName,
    long?     DocumentFileSize,
    DateTime? GeneratedAt,
    DateTime? SentAt,
    DateTime  CreatedAt,
    DateTime? RetentionUntil,
    string?   Notes,    // motivo de rechazo — solo presente cuando Status == 'rejected'
    string?   SignedByName,  // firma electrónica — solo presente cuando el colaborador firmó desde el portal
    DateTime? SignedAt);

public record SuggestedPayoutAmountDto(
    decimal Amount,
    decimal Hours,
    bool    HasTimeEntries);

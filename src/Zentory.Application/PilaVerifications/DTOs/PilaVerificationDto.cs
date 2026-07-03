namespace Zentory.Application.PilaVerifications.DTOs;

public record PilaComplianceRowDto(
    Guid     CollaboratorId,
    string   CollaboratorName,
    string?  CollaboratorRole,
    string   CollaboratorType,  // 'hourly_contractor' | 'fixed_contractor' — nunca 'employee', ya filtrado en la query
    string?  CollaboratorEmail,
    decimal? HourlyRate,
    decimal? MonthlyRate,
    string   Currency,
    Guid?    VerificationId,
    string   Status,   // 'no_aplica' (derivado, bajo 1 SMLV) | 'pendiente' (derivado, sin fila) | 'solicitada' | 'recibida' | 'verificada' | 'rechazada'
    DateTime? RequestedAt,
    DateTime? ReceivedAt,
    DateTime? VerifiedAt,
    string    PayoutInvoiceStatus,       // 'ninguna' | 'generated' | 'sent' | 'uploaded_manually' — última cuenta de cobro conocida
    string?   PayoutInvoiceLastPeriod);

public record PilaVerificationDto(
    Guid      Id,
    Guid      CollaboratorId,
    string    Period,
    string    Status,
    DateTime  RequestedAt,
    DateTime? ReceivedAt,
    DateTime? VerifiedAt,
    string?   Notes,
    string?   DocumentFileName,
    long?     DocumentFileSize,
    DateTime? RetentionUntil,
    string    Source);

public record PilaVerificationDocumentDto(
    Guid      Id,
    string?   FileName,
    long?     FileSize,
    string?   ContentType,
    DateTime  UploadedAt,
    DateTime? RetentionUntil);

public record PresignedUploadUrlDto(
    string   UploadUrl,
    string   StorageKey,
    DateTime ExpiresAt);

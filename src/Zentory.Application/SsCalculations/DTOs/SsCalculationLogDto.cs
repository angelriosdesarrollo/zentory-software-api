namespace Zentory.Application.SsCalculations.DTOs;

public record SsCalculationLogDto(
    Guid      Id,
    string    Period,
    decimal   Income,
    string    Currency,
    string    Result,   // JSON: { ibc, salud, pension, arl }
    decimal   TotalContribution,
    decimal?  SmlvUsed,
    string    Status,   // 'calculado' | 'marcado_radicado'
    DateTime? FiledAt,
    string?   DocumentFileName,
    long?     DocumentFileSize);

public record SsCalculationUploadUrlDto(
    string   UploadUrl,
    string   StorageKey,
    DateTime ExpiresAt);

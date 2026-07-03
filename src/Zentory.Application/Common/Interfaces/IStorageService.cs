namespace Zentory.Application.Common.Interfaces;

public interface IStorageService
{
    Task<PresignedUploadUrl> GeneratePresignedUploadUrlAsync(
        string   key,
        string   contentType,
        TimeSpan expiresIn,
        CancellationToken ct = default);

    Task<string> GeneratePresignedDownloadUrlAsync(
        string   key,
        TimeSpan expiresIn,
        CancellationToken ct = default);

    /// <summary>Subida directa server-side (ej. PDFs generados por el backend, no por el navegador).</summary>
    Task UploadAsync(
        string   key,
        Stream   content,
        string   contentType,
        CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}

public record PresignedUploadUrl(string UploadUrl, string Key, DateTime ExpiresAt);

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

    /// <summary>Todas las claves que empiezan por <paramref name="prefix"/> (comparación ordinal), en orden ordinal.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default);

    /// <summary>Copia server-side; el destino se sobrescribe si existe (PLAN-0018 RF-79: PDF al archivo legal).</summary>
    Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default);

    /// <summary>
    /// Borra todos los archivos de una organización (PLAN-0018 RF-78 5, SR-0018.27). El prefijo debe ser exactamente
    /// "{organizationId}/" con un Guid no vacío; cualquier otro valor lanza antes de listar, sin borrar nada. Es
    /// reintentable: repetirlo sobre un prefijo ya vacío devuelve 0. Las implementaciones no lo sobrescriben, para que la
    /// validación sea una sola.
    /// </summary>
    async Task<int> DeleteOrganizationFilesAsync(string prefix, CancellationToken ct = default)
    {
        OrganizationStoragePrefix.EnsureValid(prefix);

        var keys = await ListKeysAsync(prefix, ct);
        foreach (var key in keys)
            await DeleteAsync(key, ct);
        return keys.Count;
    }
}

public record PresignedUploadUrl(string UploadUrl, string Key, DateTime ExpiresAt);

// Prefijo de los archivos de una organización: "{organizationId}/", igual que StorageKeyBuilder (PLAN-0018 RF-78 5).
// Un prefijo vacío, "/" o el Guid sin barra ("{A}" también casaría con "{A}x/...") borraría archivos de otros, así que
// solo se acepta la forma canónica exacta.
public static class OrganizationStoragePrefix
{
    public static string For(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("El prefijo de almacenamiento necesita una organización.", nameof(organizationId));
        return $"{organizationId}/";
    }

    public static bool IsValid(string? prefix)
    {
        if (prefix is null || prefix.Length != 37 || prefix[^1] != '/')
            return false;
        var id = prefix[..^1];
        return Guid.TryParseExact(id, "D", out var organizationId)
            && organizationId != Guid.Empty
            && string.Equals(id, organizationId.ToString(), StringComparison.Ordinal);
    }

    public static void EnsureValid(string? prefix)
    {
        if (!IsValid(prefix))
            throw new ArgumentException(
                "El borrado por prefijo exige exactamente \"{organizationId}/\" con un Guid no vacío.", nameof(prefix));
    }
}

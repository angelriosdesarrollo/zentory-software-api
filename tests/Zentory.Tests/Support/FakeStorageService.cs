using System.Collections.Concurrent;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Tests.Support;

// Doble en memoria de IStorageService para el host de pruebas (PLAN-0018 RF-19 d): nunca llama a R2 ni escribe en
// disco, así ningún test deja archivos bajo src/Zentory.API/App_Data. Guarda cada objeto subido para que un test pueda
// afirmar qué se guardó o se borró.
public sealed class FakeStorageService : IStorageService
{
    public const string BaseUrl = "https://storage.zentory.test";

    public sealed record StoredObject(string Key, string ContentType, byte[] Content);

    private readonly ConcurrentDictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, StoredObject> Objects => new Dictionary<string, StoredObject>(_objects);

    public Task<PresignedUploadUrl> GeneratePresignedUploadUrlAsync(
        string key, string contentType, TimeSpan expiresIn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new PresignedUploadUrl($"{BaseUrl}/upload/{key}", key, DateTime.UtcNow.Add(expiresIn)));
    }

    public Task<string> GeneratePresignedDownloadUrlAsync(string key, TimeSpan expiresIn, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult($"{BaseUrl}/download/{key}");
    }

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        _objects[key] = new StoredObject(key, contentType, buffer.ToArray());
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<string> keys = _objects.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(keys);
    }

    // Como R2 y el disco local: copiar una clave inexistente falla.
    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_objects.TryGetValue(sourceKey, out var source))
            throw new FileNotFoundException("No existe el objeto de origen.", sourceKey);
        _objects[destinationKey] = source with { Key = destinationKey };
        return Task.CompletedTask;
    }

    // Siembra directa de un objeto, sin pasar por UploadAsync (p. ej. una subida sin fila asociada).
    public void Seed(string key, string contentType = "application/octet-stream", byte[]? content = null)
        => _objects[key] = new StoredObject(key, contentType, content ?? []);
}

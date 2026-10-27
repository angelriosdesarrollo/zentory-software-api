using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

// Resuelve dónde vive el "bucket" local en disco y evita que un key con ".." escape de
// ahí — compartido entre LocalDiskStorageService y DevLocalStorageController (API), que
// necesita reconstruir la misma ruta para servir los archivos de vuelta por GET.
public static class LocalDevStoragePaths
{
    public static string RootPath(IWebHostEnvironment env)
    {
        var root = Path.Combine(env.ContentRootPath, "App_Data", "dev-storage");
        Directory.CreateDirectory(root);
        return root;
    }

    public static string ResolveSafePath(string rootPath, string key)
    {
        var safeKey  = key.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, safeKey));
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid storage key.");
        return fullPath;
    }
}

// Fallback de IStorageService para desarrollo local sin credenciales R2 reales
// (mismo espíritu que Database:UseInMemory para EF) — guarda los archivos en disco y
// los sirve de vuelta vía DevLocalStorageController. Nunca se usa si R2:AccountId está
// configurado (ver ServiceCollectionExtensions.AddInfrastructure).
public sealed class LocalDiskStorageService : IStorageService
{
    private readonly string               _rootPath;
    private readonly IHttpContextAccessor _accessor;

    public LocalDiskStorageService(IWebHostEnvironment env, IHttpContextAccessor accessor)
    {
        _rootPath = LocalDevStoragePaths.RootPath(env);
        _accessor = accessor;
    }

    private string BaseUrl()
    {
        var request = _accessor.HttpContext?.Request
            ?? throw new InvalidOperationException("LocalDiskStorageService requires an active HTTP request.");
        return $"{request.Scheme}://{request.Host}";
    }

    public Task<PresignedUploadUrl> GeneratePresignedUploadUrlAsync(
        string key, string contentType, TimeSpan expiresIn, CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow.Add(expiresIn);
        var url = $"{BaseUrl()}/api/v1/public/dev/local-storage/{key}";
        return Task.FromResult(new PresignedUploadUrl(url, key, expiresAt));
    }

    public Task<string> GeneratePresignedDownloadUrlAsync(string key, TimeSpan expiresIn, CancellationToken ct = default)
        => Task.FromResult($"{BaseUrl()}/api/v1/public/dev/local-storage/{key}");

    public async Task UploadAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = LocalDevStoragePaths.ResolveSafePath(_rootPath, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = LocalDevStoragePaths.ResolveSafePath(_rootPath, key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Recorre solo la carpeta del último "/" del prefijo y filtra por el prefijo completo (ordinal, como R2).
    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
    {
        var slash     = prefix.LastIndexOf('/');
        var directory = slash < 0 ? _rootPath : LocalDevStoragePaths.ResolveSafePath(_rootPath, prefix[..slash]);
        if (!Directory.Exists(directory))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var keys = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_rootPath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        var source      = LocalDevStoragePaths.ResolveSafePath(_rootPath, sourceKey);
        var destination = LocalDevStoragePaths.ResolveSafePath(_rootPath, destinationKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return Task.CompletedTask;
    }
}

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Services;

public sealed class CloudflareR2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string    _bucketName;

    public CloudflareR2StorageService(IConfiguration configuration)
    {
        var accountId = configuration["R2:AccountId"] ?? string.Empty;
        var accessKey = configuration["R2:AccessKey"]  ?? string.Empty;
        var secretKey = configuration["R2:SecretKey"]  ?? string.Empty;
        _bucketName   = configuration["R2:BucketName"] ?? "zentory-documents";

        var s3Config = new AmazonS3Config
        {
            ServiceURL     = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true,
            // R2 solo soporta presigned URLs SigV4; sin esto, el SDK genera SigV2
            // (AWSAccessKeyId=...) por defecto al no reconocer el endpoint como AWS real.
            AuthenticationRegion = "auto",
        };

        _s3 = new AmazonS3Client(accessKey, secretKey, s3Config);
    }

    public Task<PresignedUploadUrl> GeneratePresignedUploadUrlAsync(
        string   key,
        string   contentType,
        TimeSpan expiresIn,
        CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow.Add(expiresIn);
        var request = new GetPreSignedUrlRequest
        {
            BucketName  = _bucketName,
            Key         = key,
            Verb        = HttpVerb.PUT,
            ContentType = contentType,
            Expires     = expiresAt,
        };

        return Task.FromResult(new PresignedUploadUrl(_s3.GetPreSignedURL(request), key, expiresAt));
    }

    public Task<string> GeneratePresignedDownloadUrlAsync(
        string   key,
        TimeSpan expiresIn,
        CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key        = key,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.Add(expiresIn),
        };

        return Task.FromResult(_s3.GetPreSignedURL(request));
    }

    public async Task UploadAsync(
        string   key,
        Stream   content,
        string   contentType,
        CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName  = _bucketName,
            Key         = key,
            InputStream = content,
            ContentType = contentType,
            // R2 no implementa la firma de payload en streaming/chunks que el SDK usa
            // por defecto en versiones recientes; hay que forzar el modo no-streaming.
            DisablePayloadSigning = true,
        };
        await _s3.PutObjectAsync(request, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => _s3.DeleteObjectAsync(_bucketName, key, ct);

    // ListObjectsV2 devuelve como mucho 1000 claves por página; se recorren todas con el token de continuación.
    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
    {
        var keys = new List<string>();
        var request = new ListObjectsV2Request { BucketName = _bucketName, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await _s3.ListObjectsV2Async(request, ct);
            if (response.S3Objects is not null)
                keys.AddRange(response.S3Objects.Select(o => o.Key).Where(k => k.StartsWith(prefix, StringComparison.Ordinal)));
            if (response.IsTruncated != true)
                break;
            // Una página truncada sin token repetiría la misma página para siempre: mejor fallar (la purga se reintenta).
            if (string.IsNullOrEmpty(response.NextContinuationToken))
                throw new InvalidOperationException("R2 devolvió una página truncada sin token de continuación.");
            request.ContinuationToken = response.NextContinuationToken;
        } while (true);

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
        => _s3.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket      = _bucketName,
            SourceKey         = sourceKey,
            DestinationBucket = _bucketName,
            DestinationKey    = destinationKey,
        }, ct);
}

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
        };
        await _s3.PutObjectAsync(request, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
        => _s3.DeleteObjectAsync(_bucketName, key, ct);
}

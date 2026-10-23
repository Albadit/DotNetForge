using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Shared.Configuration;

namespace DotNetForge.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> on any S3-compatible service - Cloudflare R2, AWS S3, MinIO, SeaweedFS, Supabase Storage
/// (.docs/features/media-storage.md). Objects stay private; browsers download through short-lived presigned URLs.
/// </summary>
/// <remarks>
/// The SDK's optional request checksums and <c>aws-chunked</c> uploads are turned off because several S3-compatible
/// services reject them, and presigned URLs use the endpoint's own scheme (the SDK would presign <c>https://</c> for a
/// plain-HTTP development endpoint otherwise).
/// </remarks>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly Protocol _protocol;

    public S3FileStorage(StorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _bucket = settings.S3Bucket;

        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.S3ForcePathStyle,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (string.IsNullOrWhiteSpace(settings.S3ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.S3Region);
            _protocol = Protocol.HTTPS;
        }
        else
        {
            config.ServiceURL = settings.S3ServiceUrl;
            config.AuthenticationRegion = settings.S3Region;
            _protocol = settings.S3ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? Protocol.HTTP
                : Protocol.HTTPS;
        }

        _client = new AmazonS3Client(new BasicAWSCredentials(settings.S3AccessKeyId, settings.S3SecretAccessKey), config);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
        ArgumentNullException.ThrowIfNull(content);

        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            UseChunkEncoding = false,
        }, cancellationToken);
    }

    public async Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, cancellationToken);
            return new StoredFile(response.ResponseStream, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
        await _client.DeleteObjectAsync(_bucket, key, cancellationToken); // S3 deletes are idempotent
    }

    public async Task<Uri?> GetDownloadUrlAsync(
        string key, string contentDisposition, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = _protocol,
            Expires = DateTime.UtcNow.Add(lifetime),
        };
        request.ResponseHeaderOverrides.ContentDisposition = contentDisposition;

        return new Uri(await _client.GetPreSignedURLAsync(request));
    }

    public void Dispose() => _client.Dispose();
}

using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace S3Storage;

/// <summary>
/// Entry point of the S3 media storage connector. While it is enabled the host stores and serves all media through
/// <see cref="S3BucketStorage"/>; when it is disabled the host's own storage is used again. Objects are never moved
/// between the two, so switch on a fresh site or copy the objects yourself.
/// </summary>
public sealed class S3StorageConnector : IExtension
{
    public const string AccessKeyVariable = "S3_CONNECTOR_ACCESS_KEY_ID";
    public const string SecretKeyVariable = "S3_CONNECTOR_SECRET_ACCESS_KEY";

    public void ConfigureServices(IServiceCollection services, ExtensionContext context) =>
        // Created on first use, so a missing secret only fails media operations - with a clear message - not startup.
        services.AddMediaStorage(context, _ => new S3BucketStorage(
            context.GetString("serviceUrl"),
            context.GetString("bucket") is { Length: > 0 } bucket ? bucket : throw Missing("the \"bucket\" setting"),
            context.GetString("region", "auto")!,
            context.GetBool("forcePathStyle"),
            context.GetEnvironmentValue(AccessKeyVariable) ?? throw Missing(AccessKeyVariable),
            context.GetEnvironmentValue(SecretKeyVariable) ?? throw Missing(SecretKeyVariable)));

    private static InvalidOperationException Missing(string what) =>
        new($"The S3 media storage connector needs {what} (secrets go in .env).");
}

/// <summary><see cref="IFileStorage"/> on one S3 bucket. Objects stay private; downloads use presigned URLs.</summary>
public sealed class S3BucketStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly Protocol _protocol;

    public S3BucketStorage(string? serviceUrl, string bucket, string region, bool forcePathStyle, string accessKey, string secretKey)
    {
        _bucket = bucket;
        var config = new AmazonS3Config
        {
            ForcePathStyle = forcePathStyle,
            // Several S3-compatible services reject the SDK's optional checksums.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        if (string.IsNullOrWhiteSpace(serviceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }
        else
        {
            config.ServiceURL = serviceUrl;
            config.AuthenticationRegion = region;
        }

        _protocol = serviceUrl?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ? Protocol.HTTP : Protocol.HTTPS;
        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
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
        await _client.DeleteObjectAsync(_bucket, key, cancellationToken);
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

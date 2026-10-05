using DotNetForge.Abstractions.Storage;
using DotNetForge.Infrastructure.Storage;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// The behaviour every <see cref="IFileStorage"/> provider must have (.docs/features/media-storage.md). Run for the
/// local provider always, and for S3 when <c>DNF_TEST_S3_*</c> points at a reachable bucket (see <see cref="S3FactAttribute"/>).
/// </summary>
internal static class FileStorageContract
{
    public static async Task SaveThenReadReturnsTheSameBytes(IFileStorage storage)
    {
        var key = NewKey(".txt");
        await storage.SaveAsync(key, new MemoryStream("hello storage"u8.ToArray()), "text/plain");

        await using var stored = await storage.OpenReadAsync(key);
        Assert.NotNull(stored);
        Assert.Equal(13, stored!.Length);
        using var copy = new MemoryStream();
        await stored.Content.CopyToAsync(copy);
        Assert.Equal("hello storage"u8.ToArray(), copy.ToArray());
        await storage.DeleteAsync(key);
    }

    public static async Task SaveOverwritesAnExistingObject(IFileStorage storage)
    {
        var key = NewKey(".txt");
        await storage.SaveAsync(key, new MemoryStream("first"u8.ToArray()), "text/plain");
        await storage.SaveAsync(key, new MemoryStream("second!"u8.ToArray()), "text/plain");

        await using var stored = await storage.OpenReadAsync(key);
        Assert.Equal(7, stored!.Length);
        await storage.DeleteAsync(key);
    }

    public static async Task MissingObjectReadsAsNull(IFileStorage storage) =>
        Assert.Null(await storage.OpenReadAsync(NewKey(".bin")));

    public static async Task DeleteRemovesTheObjectAndIsIdempotent(IFileStorage storage)
    {
        var key = NewKey(".bin");
        await storage.SaveAsync(key, new MemoryStream(new byte[] { 1, 2, 3 }), "application/octet-stream");

        await storage.DeleteAsync(key);
        await storage.DeleteAsync(key); // deleting a missing object is not an error

        Assert.Null(await storage.OpenReadAsync(key));
    }

    public static async Task InvalidKeysAreRejected(IFileStorage storage)
    {
        foreach (var key in new[] { "../escape.txt", "a/../../b", "/absolute", "a//b", "a\\b", "", "spaces in key" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                storage.SaveAsync(key, new MemoryStream(new byte[] { 1 }), "text/plain"));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(key));
        }
    }

    private static string NewKey(string extension) => $"tests/{Guid.NewGuid():N}{extension}";
}

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dnf-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests() => _storage = new LocalFileStorage(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact] public Task Save_then_read() => FileStorageContract.SaveThenReadReturnsTheSameBytes(_storage);
    [Fact] public Task Save_overwrites() => FileStorageContract.SaveOverwritesAnExistingObject(_storage);
    [Fact] public Task Missing_reads_as_null() => FileStorageContract.MissingObjectReadsAsNull(_storage);
    [Fact] public Task Delete_is_idempotent() => FileStorageContract.DeleteRemovesTheObjectAndIsIdempotent(_storage);
    [Fact] public Task Invalid_keys_are_rejected() => FileStorageContract.InvalidKeysAreRejected(_storage);

    [Fact]
    public async Task Files_stay_inside_the_root_and_no_temp_files_are_left()
    {
        await _storage.SaveAsync("t/2026/10/abc.png", new MemoryStream(new byte[] { 1 }), "image/png");

        var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
        Assert.Equal(Path.Combine(_root, "t", "2026", "10", "abc.png"), Assert.Single(files));
    }

    [Fact]
    public async Task A_failed_upload_leaves_no_partial_object()
    {
        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync("t/broken.bin", new FailingStream(), "application/octet-stream"));

        Assert.Null(await _storage.OpenReadAsync("t/broken.bin"));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Local_storage_cannot_issue_download_urls() =>
        Assert.Null(await _storage.GetDownloadUrlAsync("t/a.png", "inline", TimeSpan.FromMinutes(5)));

    private sealed class FailingStream : MemoryStream
    {
        public FailingStream() : base(new byte[1024]) { }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            throw new IOException("Connection reset while uploading.");
    }
}

public sealed class StorageKeyTests
{
    [Theory]
    [InlineData("a.png", true)]
    [InlineData("0f3c/2026/10/9b1e.png", true)]
    [InlineData("tenant_1/file-name.v2.pdf", true)]
    [InlineData("", false)]
    [InlineData("/leading", false)]
    [InlineData("trailing/", false)]
    [InlineData("a//b", false)]
    [InlineData("../up", false)]
    [InlineData("a/./b", false)]
    [InlineData("back\\slash", false)]
    [InlineData("späce", false)]
    [InlineData("query?x=1", false)]
    public void Validates_keys(string key, bool expected) => Assert.Equal(expected, StorageKey.IsValid(key));

    [Fact]
    public void Rejects_overlong_keys() => Assert.False(StorageKey.IsValid(new string('a', StorageKey.MaxLength + 1)));
}

public sealed class S3FileStorageTests
{
    private static S3FileStorage CreateOffline() => new(new StorageSettings
    {
        Provider = StorageProvider.S3,
        S3ServiceUrl = "https://example-account.r2.cloudflarestorage.com",
        S3Bucket = "media",
        S3AccessKeyId = "AKIDEXAMPLE",
        S3SecretAccessKey = "secret-example",
        S3Region = "auto",
    });

    [Fact]
    public async Task Presigned_urls_are_signed_short_lived_and_carry_the_disposition()
    {
        using var storage = CreateOffline();

        var url = await storage.GetDownloadUrlAsync("t/2026/10/abc.pdf", "attachment; filename=\"report.pdf\"",
            TimeSpan.FromMinutes(5));

        Assert.NotNull(url);
        Assert.Equal("https", url!.Scheme);
        Assert.Contains("abc.pdf", url.AbsolutePath);
        var query = Uri.UnescapeDataString(url.Query);
        Assert.Contains("X-Amz-Signature=", query);
        Assert.Contains("X-Amz-Expires=300", query);
        Assert.Contains("response-content-disposition=attachment; filename=\"report.pdf\"", query);
        Assert.DoesNotContain("secret-example", url.ToString());
    }

    [Fact]
    public async Task Presigned_urls_follow_a_plain_http_endpoint()
    {
        // Regression: the SDK presigns https:// by default, which breaks local HTTP endpoints (MinIO, SeaweedFS).
        using var storage = new S3FileStorage(new StorageSettings
        {
            Provider = StorageProvider.S3,
            S3ServiceUrl = "http://localhost:8333",
            S3Bucket = "media",
            S3AccessKeyId = "dev",
            S3SecretAccessKey = "dev",
            S3ForcePathStyle = true,
        });

        var url = await storage.GetDownloadUrlAsync("t/a.png", "inline", TimeSpan.FromMinutes(1));

        Assert.Equal("http://localhost:8333/media/t/a.png", url!.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task Invalid_keys_are_rejected_before_any_request()
    {
        using var storage = CreateOffline();
        await Assert.ThrowsAsync<ArgumentException>(() => storage.GetDownloadUrlAsync("../x", "inline", TimeSpan.FromMinutes(1)));
    }

    // Live contract tests: run only when DNF_TEST_S3_* is configured (e.g. a local MinIO, see .docs/guides/testing.md).
    [S3Fact] public Task Save_then_read() => WithLive(FileStorageContract.SaveThenReadReturnsTheSameBytes);
    [S3Fact] public Task Save_overwrites() => WithLive(FileStorageContract.SaveOverwritesAnExistingObject);
    [S3Fact] public Task Missing_reads_as_null() => WithLive(FileStorageContract.MissingObjectReadsAsNull);
    [S3Fact] public Task Delete_is_idempotent() => WithLive(FileStorageContract.DeleteRemovesTheObjectAndIsIdempotent);

    [S3Fact]
    public async Task Presigned_url_downloads_the_object()
    {
        using var storage = S3FactAttribute.CreateLive();
        var key = $"tests/{Guid.NewGuid():N}.txt";
        await storage.SaveAsync(key, new MemoryStream("via presigned url"u8.ToArray()), "text/plain");
        try
        {
            var url = await storage.GetDownloadUrlAsync(key, "attachment; filename=\"x.txt\"", TimeSpan.FromMinutes(1));
            using var http = new HttpClient();
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            Assert.Equal("via presigned url", await response.Content.ReadAsStringAsync());
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        }
        finally
        {
            await storage.DeleteAsync(key);
        }
    }

    private static async Task WithLive(Func<IFileStorage, Task> test)
    {
        using var storage = S3FactAttribute.CreateLive();
        await test(storage);
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that is skipped unless <c>DNF_TEST_S3_SERVICE_URL</c>, <c>DNF_TEST_S3_BUCKET</c>,
/// <c>DNF_TEST_S3_ACCESS_KEY_ID</c> and <c>DNF_TEST_S3_SECRET_ACCESS_KEY</c> are set.
/// </summary>
public sealed class S3FactAttribute : FactAttribute
{
    public S3FactAttribute()
    {
        if (Settings() is null)
        {
            Skip = "Set DNF_TEST_S3_SERVICE_URL, _BUCKET, _ACCESS_KEY_ID and _SECRET_ACCESS_KEY to run live S3 tests.";
        }
    }

    public static S3FileStorage CreateLive() => new(Settings() ?? throw new InvalidOperationException("S3 test settings missing."));

    private static StorageSettings? Settings()
    {
        string? Get(string name) => Environment.GetEnvironmentVariable("DNF_TEST_S3_" + name) is { Length: > 0 } v ? v : null;
        var (url, bucket, key, secret) = (Get("SERVICE_URL"), Get("BUCKET"), Get("ACCESS_KEY_ID"), Get("SECRET_ACCESS_KEY"));
        return url is null || bucket is null || key is null || secret is null
            ? null
            : new StorageSettings
            {
                Provider = StorageProvider.S3,
                S3ServiceUrl = url,
                S3Bucket = bucket,
                S3AccessKeyId = key,
                S3SecretAccessKey = secret,
                S3Region = Get("REGION") ?? "auto",
                S3ForcePathStyle = bool.TryParse(Get("FORCE_PATH_STYLE"), out var f) && f,
            };
    }
}

namespace DotNetForge.Abstractions.Storage;

/// <summary>
/// Provider-neutral object storage for files the application creates at runtime (uploaded media). The deployment
/// directory is read-only, so nothing may be written there; every provider stores objects elsewhere
/// (.docs/features/media-storage.md). Every member validates <paramref name="key"/> with <see cref="StorageKey"/>.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores <paramref name="content"/> under <paramref name="key"/>, replacing any existing object.</summary>
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens the object for reading, or returns <c>null</c> when it does not exist.</summary>
    Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes the object. Deleting a missing object is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived signed URL the browser can download the object from directly, or <c>null</c> when the provider
    /// cannot sign URLs (the caller then streams <see cref="OpenReadAsync"/> itself).
    /// </summary>
    Task<Uri?> GetDownloadUrlAsync(
        string key, string contentDisposition, TimeSpan lifetime, CancellationToken cancellationToken = default);
}

/// <summary>An open stored object. Disposing it releases the stream (and the provider's connection).</summary>
public sealed class StoredFile(Stream content, long length) : IAsyncDisposable, IDisposable
{
    public Stream Content { get; } = content;

    /// <summary>The object size in bytes.</summary>
    public long Length { get; } = length;

    public ValueTask DisposeAsync() => Content.DisposeAsync();

    public void Dispose() => Content.Dispose();
}

/// <summary>
/// Storage key rules shared by every provider: 1-<see cref="MaxLength"/> characters, <c>/</c>-separated segments of
/// <c>[A-Za-z0-9._-]</c>, no empty, <c>.</c> or <c>..</c> segments - so a key can never traverse or produce odd object
/// names.
/// </summary>
public static class StorageKey
{
    public const int MaxLength = 512;

    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength)
        {
            return false;
        }

        foreach (var segment in key.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }

            foreach (var c in segment)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Throws <see cref="ArgumentException"/> when <paramref name="key"/> is not a valid storage key.</summary>
    public static void EnsureValid(string? key)
    {
        if (!IsValid(key))
        {
            throw new ArgumentException($"'{key}' is not a valid storage key.", nameof(key));
        }
    }
}

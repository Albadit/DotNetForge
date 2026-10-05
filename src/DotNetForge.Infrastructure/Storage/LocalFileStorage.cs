using DotNetForge.Abstractions.Storage;

namespace DotNetForge.Infrastructure.Storage;

/// <summary>
/// <see cref="IFileStorage"/> on a local directory: the development default (<c>storage/media</c>) or, in
/// production, an explicitly configured absolute path on a writable volume - never the deployment directory
/// (.docs/features/media-storage.md). It cannot issue download URLs, so the application streams files itself.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temporary sibling and move it into place, so a failed or cancelled upload never leaves a
        // truncated object under the real key.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 81920, useAsync: true);
            return Task.FromResult<StoredFile?>(new StoredFile(stream, stream.Length));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<StoredFile?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(Resolve(key)); // no-op when missing
        return Task.CompletedTask;
    }

    public Task<Uri?> GetDownloadUrlAsync(
        string key, string contentDisposition, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
        return Task.FromResult<Uri?>(null);
    }

    private string Resolve(string key)
    {
        StorageKey.EnsureValid(key);
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));

        // StorageKey already rejects traversal; keep the containment check as defence in depth.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Storage key '{key}' resolves outside the storage root.", nameof(key));
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a leftover *.tmp file is harmless and never served (keys can't end in a random suffix).
        }
    }
}

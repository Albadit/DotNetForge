using DotNetForge.Abstractions.Storage;
using DotNetForge.Extensions;
using DotNetForge.Sdk;

namespace DotNetForge.Web.Services;

/// <summary>
/// The <see cref="IFileStorage"/> the application uses (.docs/features/media-storage.md#storage-connectors): the
/// storage of the first enabled extension that offers one (<c>AddMediaStorage</c>), otherwise the host's own storage
/// (S3, or local in Development). The choice is made per call, so enabling or disabling a connector takes effect
/// without a restart. Objects are not moved: media stored before a switch stays in the previous storage.
/// </summary>
public sealed class ExtensionMediaStorage : IFileStorage, IDisposable
{
    /// <summary>The DI key of the host's own storage.</summary>
    public const string CoreStorageKey = "core";

    private readonly IFileStorage _core;
    private readonly IReadOnlyList<(MediaStorageContribution Contribution, Lazy<IFileStorage> Storage)> _connectors;
    private readonly ExtensionCatalog _catalog;

    public ExtensionMediaStorage(IFileStorage core, IEnumerable<MediaStorageContribution> contributions,
        ExtensionCatalog catalog, IServiceProvider services)
    {
        _core = core;
        _catalog = catalog;
        // PublicationOnly: a factory that throws (e.g. bad connector settings) is retried on the next call, not cached.
        _connectors = contributions
            .Select(c => (c, new Lazy<IFileStorage>(() => c.Factory(services), LazyThreadSafetyMode.PublicationOnly)))
            .ToList();
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken)).SaveAsync(key, content, contentType, cancellationToken);

    public async Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken)).OpenReadAsync(key, cancellationToken);

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken)).DeleteAsync(key, cancellationToken);

    public async Task<Uri?> GetDownloadUrlAsync(
        string key, string contentDisposition, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken)).GetDownloadUrlAsync(key, contentDisposition, lifetime, cancellationToken);

    /// <summary>The storage in use right now.</summary>
    public async Task<IFileStorage> CurrentAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (contribution, storage) in _connectors)
        {
            if (await _catalog.IsActiveAsync(contribution.ExtensionId, cancellationToken))
            {
                return storage.Value;
            }
        }

        return _core;
    }

    public void Dispose()
    {
        foreach (var (_, storage) in _connectors)
        {
            if (storage.IsValueCreated && storage.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}

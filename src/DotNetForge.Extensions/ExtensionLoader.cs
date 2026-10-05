using System.Text.Json;
using DotNetForge.Core.Extensions;
using DotNetForge.Shared.Manifests;

namespace DotNetForge.Extensions;

/// <summary>
/// Discovers extensions on disk under the configured <c>extensions/</c> folder. Every subdirectory is scanned for a
/// <see cref="ExtensionManifest.FileName"/>; the manifest is parsed and validated. Invalid or unparseable manifests
/// are reported but never loaded (.docs/features/extensions.md).
/// </summary>
/// <remarks>
/// The sidebar asks for extensions on every admin screen, so results are cached. A <see cref="FileSystemWatcher"/>
/// invalidates the cache when any manifest is created, changed, renamed or deleted; if the watcher cannot be
/// created (e.g. an unsupported file system), every call rescans so results are never stale.
/// </remarks>
public sealed class ExtensionLoader : IExtensionLoader, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IManifestValidator _validator;
    private readonly string _root;
    private readonly FileSystemWatcher? _watcher;
    private volatile IReadOnlyList<DiscoveredExtension>? _cached;
    private int _version;

    public ExtensionLoader(IManifestValidator validator, string extensionsRoot)
    {
        _validator = validator;
        _root = extensionsRoot;
        _watcher = TryWatch(extensionsRoot);
    }

    public IReadOnlyList<DiscoveredExtension> Discover()
    {
        if (_watcher is null)
        {
            return Scan();
        }

        if (_cached is { } cached)
        {
            return cached;
        }

        // Only cache a scan that no change notification overlapped, so a stale result can't stick.
        var version = Volatile.Read(ref _version);
        var result = Scan();
        if (Volatile.Read(ref _version) == version)
        {
            _cached = result;
        }

        return result;
    }

    public DiscoveredExtension? FindAdminExtension(string id) =>
        Discover().FirstOrDefault(d =>
            d.IsValid &&
            string.Equals(d.Manifest!.Type, "admin", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.Manifest.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => _watcher?.Dispose();

    private IReadOnlyList<DiscoveredExtension> Scan()
    {
        if (string.IsNullOrWhiteSpace(_root) || !Directory.Exists(_root))
        {
            return Array.Empty<DiscoveredExtension>();
        }

        return Directory.EnumerateFiles(_root, ExtensionManifest.FileName, SearchOption.AllDirectories)
            .Select(Load)
            .ToList();
    }

    private FileSystemWatcher? TryWatch(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(root, ExtensionManifest.FileName)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                               NotifyFilters.Size,
            };

            void Invalidate(object? sender, EventArgs e)
            {
                Interlocked.Increment(ref _version);
                _cached = null;
            }

            watcher.Changed += Invalidate;
            watcher.Created += Invalidate;
            watcher.Deleted += Invalidate;
            watcher.Renamed += Invalidate;
            watcher.Error += Invalidate; // buffer overflow: we may have missed events
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or
                                       UnauthorizedAccessException)
        {
            return null;
        }
    }

    private DiscoveredExtension Load(string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions);

            if (manifest is null)
            {
                var empty = new ValidationResult();
                empty.Add("Manifest", "Manifest could not be parsed.");
                return new DiscoveredExtension { Path = manifestPath, Manifest = null, Validation = empty };
            }

            return new DiscoveredExtension
            {
                Path = manifestPath,
                Manifest = manifest,
                Validation = _validator.Validate(manifest),
            };
        }
        catch (JsonException ex)
        {
            var invalid = new ValidationResult();
            invalid.Add("Manifest", $"Invalid JSON: {ex.Message}");
            return new DiscoveredExtension { Path = manifestPath, Manifest = null, Validation = invalid };
        }
    }
}

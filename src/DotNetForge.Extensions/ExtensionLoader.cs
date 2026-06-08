using System.Text.Json;
using DotNetForge.Core.Extensions;
using DotNetForge.Shared.Manifests;

namespace DotNetForge.Extensions;

/// <summary>
/// Discovers extensions on disk under the dedicated <c>extensions/</c> folder. Each subdirectory is
/// scanned for a <see cref="ExtensionManifest.FileName"/>; the manifest is parsed and validated.
/// Invalid or unparseable manifests are reported but never loaded (architecture.md, extensions.md).
/// </summary>
public sealed class ExtensionLoader : IExtensionLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IManifestValidator _validator;

    public ExtensionLoader(IManifestValidator validator)
    {
        _validator = validator;
    }

    public IReadOnlyList<DiscoveredExtension> Discover(string extensionsRoot)
    {
        var discovered = new List<DiscoveredExtension>();

        if (string.IsNullOrWhiteSpace(extensionsRoot) || !Directory.Exists(extensionsRoot))
        {
            return discovered;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(
                     extensionsRoot, ExtensionManifest.FileName, SearchOption.AllDirectories))
        {
            discovered.Add(Load(manifestPath));
        }

        return discovered;
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

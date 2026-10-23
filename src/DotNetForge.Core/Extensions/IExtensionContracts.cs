using DotNetForge.Shared.Manifests;

namespace DotNetForge.Core.Extensions;

/// <summary>
/// Validates a parsed <see cref="ExtensionManifest"/> against the required-field and safety rules
/// before installation (.docs/features/extensions.md, .docs/features/security.md).
/// </summary>
public interface IManifestValidator
{
    ValidationResult Validate(ExtensionManifest manifest);
}

/// <summary>One extension found on disk by the loader, with its validation outcome.</summary>
public sealed class DiscoveredExtension
{
    public required string Path { get; init; }

    public ExtensionManifest? Manifest { get; init; }

    public required ValidationResult Validation { get; init; }

    public bool IsValid => Manifest is not null && Validation.IsValid;
}

/// <summary>
/// Discovers extensions under the configured <c>extensions/</c> folder, reading and validating each
/// <c>dotnetforge.extension.json</c> manifest. Invalid/unsafe extensions are surfaced but not loaded
/// (.docs/features/extensions.md). The folder is read-only at runtime. Which extensions may contribute (enabled, code
/// loaded) is answered by the host's <c>ExtensionCatalog</c>, not by discovery.
/// </summary>
public interface IExtensionLoader
{
    /// <summary>Every manifest found, valid or not.</summary>
    IReadOnlyList<DiscoveredExtension> Discover();
}

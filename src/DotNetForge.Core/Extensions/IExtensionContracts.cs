using DotNetForge.Shared.Manifests;

namespace DotNetForge.Core.Extensions;

/// <summary>
/// Validates a parsed <see cref="ExtensionManifest"/> against the required-field and safety rules
/// before installation (extensions.md, security.md).
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
/// Discovers extensions under the dedicated <c>extensions/</c> folder, reading and validating each
/// <c>dotnetforge.extension.json</c> manifest. Invalid/unsafe extensions are surfaced but not loaded
/// (architecture.md).
/// </summary>
public interface IExtensionLoader
{
    IReadOnlyList<DiscoveredExtension> Discover(string extensionsRoot);
}

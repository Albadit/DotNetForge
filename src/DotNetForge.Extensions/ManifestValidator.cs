using System.Text.RegularExpressions;
using DotNetForge.Core.Extensions;
using DotNetForge.Shared.Manifests;

namespace DotNetForge.Extensions;

/// <summary>
/// Validates an <see cref="ExtensionManifest"/> before installation. Enforces presence of the eight
/// required fields, a known extension <c>type</c>, a semver-shaped <c>version</c>, and well-formed,
/// non-empty <c>permissions</c>. Error <c>Field</c> values use the manifest's PascalCase property
/// names (.docs/features/extensions.md, .docs/guides/testing.md).
/// </summary>
public sealed partial class ManifestValidator : IManifestValidator
{
    /// <summary>The valid lowercase manifest <c>type</c> values (.docs/features/extensions.md).</summary>
    public static readonly IReadOnlyList<string> ValidTypes = new[]
    {
        "theme", "authentication", "connector", "library",
        "admin", "widget", "provider", "plugin", "module",
    };

    [GeneratedRegex(@"^\d+\.\d+\.\d+([-+].+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemVer();

    [GeneratedRegex(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*\.[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionKey();

    public ValidationResult Validate(ExtensionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var result = new ValidationResult();

        RequireNonEmpty(result, nameof(manifest.Id), manifest.Id);
        RequireNonEmpty(result, nameof(manifest.Name), manifest.Name);
        RequireNonEmpty(result, nameof(manifest.Description), manifest.Description);

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            result.Add(nameof(manifest.Version), "Version is required.");
        }
        else if (!SemVer().IsMatch(manifest.Version))
        {
            result.Add(nameof(manifest.Version), "Version must be a semantic version (e.g. 1.0.0).");
        }

        if (string.IsNullOrWhiteSpace(manifest.Type))
        {
            result.Add(nameof(manifest.Type), "Type is required.");
        }
        else if (!ValidTypes.Contains(manifest.Type, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(nameof(manifest.Type),
                $"Type must be one of: {string.Join(", ", ValidTypes)}.");
        }

        RequireNonEmpty(result, nameof(manifest.Author), manifest.Author);
        RequireNonEmpty(result, nameof(manifest.EntryPoint), manifest.EntryPoint);

        if (manifest.Permissions is null || manifest.Permissions.Length == 0)
        {
            result.Add(nameof(manifest.Permissions), "At least one permission is required.");
        }
        else
        {
            foreach (var permission in manifest.Permissions)
            {
                if (string.IsNullOrWhiteSpace(permission) || !PermissionKey().IsMatch(permission))
                {
                    result.Add(nameof(manifest.Permissions),
                        $"'{permission}' is not a valid permission key (expected dotted form, e.g. content.read).");
                }
            }
        }

        return result;
    }

    private static void RequireNonEmpty(ValidationResult result, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result.Add(field, $"{field} is required.");
        }
    }
}

using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// The CMS-side record of an installed extension - one row per Plugins-page entry (extensions.md).
/// Derived from the extension's validated <c>dotnetforge.extension.json</c> manifest.
/// </summary>
public class InstalledExtension
{
    /// <summary>The manifest <c>id</c>; primary identity.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public ExtensionType Type { get; set; }

    public string Author { get; set; } = string.Empty;

    /// <summary>Defaults to Disabled on install.</summary>
    public ExtensionStatus Status { get; set; } = ExtensionStatus.Disabled;

    public DateTime InstalledDate { get; set; } = DateTime.UtcNow;

    public bool UpdateAvailable { get; set; }

    public string PermissionsCsv { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? License { get; set; }
}

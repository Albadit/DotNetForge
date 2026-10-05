using DotNetForge.Shared.Content;

namespace DotNetForge.Web.Areas.Admin.Models;

/// <summary>Aggregated counts shown on the legacy admin dashboard (.docs/pages/dashboard.md).</summary>
public sealed class DashboardViewModel
{
    public string AppName { get; init; } = string.Empty;
    public int Users { get; init; }
    public int Roles { get; init; }
    public int Pages { get; init; }
    public int Media { get; init; }
    public int ApiTokens { get; init; }
    public int Webhooks { get; init; }
    public int Extensions { get; init; }
    public int AuditEntries { get; init; }
    public DateTime? InstalledAtUtc { get; init; }
    public string CmsVersion { get; init; } = "1.0.0";
}

/// <summary>A row on the Plugins page combining the DB record and on-disk discovery
/// (.docs/features/extensions.md).</summary>
public sealed class PluginRowViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public bool ValidManifest { get; init; } = true;
    public string? Source { get; init; }
}

/// <summary>Shown once after a token is created - the plaintext is never retrievable again.</summary>
public sealed class CreatedTokenViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Plaintext { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
}

/// <summary>Placeholder content for foundation modules whose full UI is future work.</summary>
public sealed class ModulePlaceholderViewModel
{
    public string Title { get; init; } = string.Empty;
    /// <summary>Repository-relative path of the document describing the planned module.</summary>
    public string DocPath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>The Content Manager screen: the page tree (left) and the selected page's form (right).</summary>
public sealed class ContentIndexViewModel
{
    public IReadOnlyList<PageTreeNode> Tree { get; init; } = Array.Empty<PageTreeNode>();
    public PageInput? Selected { get; init; }
    public Guid? SelectedId { get; init; }
    /// <summary>Parent options for the form's parent picker (every page except the one being edited).</summary>
    public IReadOnlyList<(Guid Id, string Label)> ParentOptions { get; init; } = Array.Empty<(Guid, string)>();
}

/// <summary>One node in the admin page tree, nested by <see cref="Children"/>.</summary>
public sealed class PageTreeNode
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public bool Published { get; init; }
    public bool Disabled { get; init; }
    public List<PageTreeNode> Children { get; } = new();
}

/// <summary>Posted by the drag-and-drop reorder JS: the new parent + position of each moved page.</summary>
public sealed class ReorderRequest
{
    public List<ReorderItem> Items { get; set; } = new();
}

public sealed class ReorderItem
{
    public Guid Id { get; set; }
    public Guid? ParentPageId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>The Media screen: upload permission and the tenant's files (.docs/pages/media.md).</summary>
public sealed class MediaIndexViewModel
{
    public bool CanUpload { get; init; }
    public IReadOnlyList<MediaRowViewModel> Files { get; init; } = Array.Empty<MediaRowViewModel>();
}

/// <summary>A row on the Media library list (.docs/features/media-storage.md).</summary>
public sealed class MediaRowViewModel
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public bool IsPublic { get; init; }
    public DateTime UploadedDate { get; init; }
    public string Url { get; init; } = string.Empty;
    public bool CanDelete { get; init; }
}

/// <summary>The Settings screen: existing key/value settings (left) and an add/update form (right).</summary>
public sealed class SettingsIndexViewModel
{
    public string AppName { get; init; } = string.Empty;
    public IReadOnlyList<(string Key, string? Value)> Settings { get; init; } = Array.Empty<(string, string?)>();
}

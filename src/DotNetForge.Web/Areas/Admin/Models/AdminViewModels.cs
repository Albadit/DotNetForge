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

    /// <summary>Quick actions offered by the dashboard (same checks as the target screens).</summary>
    public bool CanCreatePages { get; init; }
    public bool CanUpload { get; init; }

    /// <summary>The signed-in user's first name (or display name) for the greeting.</summary>
    public string Greeting { get; init; } = string.Empty;

    /// <summary>The latest audit entries of the tenant (and system-wide ones), newest first.</summary>
    public IReadOnlyList<DotNetForge.Shared.Entities.AuditLogEntry> RecentActivity { get; init; } =
        Array.Empty<DotNetForge.Shared.Entities.AuditLogEntry>();
}

/// <summary>A row on the Plugins page combining the DB record and on-disk discovery
/// (.docs/features/extensions.md).</summary>
public sealed class PluginRowViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;

    /// <summary><c>Enabled</c> or <c>Disabled</c> (the <c>InstalledExtensions</c> record).</summary>
    public string Status { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public bool ValidManifest { get; init; } = true;

    /// <summary>The manifest names an <c>assembly</c>.</summary>
    public bool HasCode { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>Why it can't run (invalid manifest, duplicate id, code not loaded), or <c>null</c>.</summary>
    public string? Problem { get; init; }

    /// <summary>The folder below <c>extensions/</c>.</summary>
    public string Folder { get; init; } = string.Empty;
    public DateTime? InstalledDate { get; init; }

    /// <summary>Whether Enable/Disable is offered (a valid manifest that is the one used for its id).</summary>
    public bool CanToggle { get; init; }

    public bool IsEnabled => Status == "Enabled";
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

    /// <summary>The Theme, Permissions and Layout tabs of the selected page; null when nothing is selected.</summary>
    public PageEditorExtras? Extras { get; init; }
}

/// <summary>What the page editor's Theme, Permissions and Layout tabs show (.docs/pages/content-manager.md).</summary>
public sealed class PageEditorExtras
{
    public IReadOnlyList<PermissionRoleOption> Roles { get; init; } = Array.Empty<PermissionRoleOption>();
    public IReadOnlySet<Guid> ViewRoles { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> EditRoles { get; init; } = new HashSet<Guid>();
    /// <summary>Every user of the tenant (the "Add user" picker).</summary>
    public IReadOnlyList<PermissionUserOption> Users { get; init; } = Array.Empty<PermissionUserOption>();
    /// <summary>The users with an allow or block on this page.</summary>
    public IReadOnlyList<DotNetForge.Web.Services.PageUserGrant> UserGrants { get; init; } = Array.Empty<DotNetForge.Web.Services.PageUserGrant>();
    public bool CanManagePermissions { get; init; }
    public IReadOnlyList<ThemeOption> Themes { get; init; } = Array.Empty<ThemeOption>();
    public bool HasLayout { get; init; }
    public bool HasDraftChanges { get; init; }
    public DateTime? LayoutPublishedDate { get; init; }
    public DateTime? DraftUpdatedDate { get; init; }
    public bool CanEditLayout { get; init; }
}

/// <summary>A role in the page permission table; <see cref="SiteOnly"/> roles (Public, Authenticated) can only view.</summary>
public sealed record PermissionRoleOption(Guid Id, string Name, bool SiteOnly);

/// <summary>A user in the page permission picker.</summary>
public sealed record PermissionUserOption(Guid Id, string Email, string Name);

/// <summary>An installed theme extension; only <see cref="Enabled"/> ones are used on the site.</summary>
public sealed record ThemeOption(string Id, string Name, string? Description, bool Enabled);

/// <summary>One node in the admin page tree, nested by <see cref="Children"/>.</summary>
public sealed class PageTreeNode
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public bool Published { get; init; }

    /// <summary>Whether the tree offers Delete for this page (the Delete action checks again, with edit blocks).</summary>
    public bool CanDelete { get; init; }

    /// <summary>The page's public URL when it can be opened on the site; null otherwise (see <see cref="NotOnSite"/>).</summary>
    public string? ViewUrl { get; set; }

    /// <summary>Why the page can't be opened on the site (disabled, a disabled parent, a dynamic URL...).</summary>
    public string? NotOnSite { get; set; }

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

    /// <summary>Whether the user may switch the file between public and private (<c>Media/update</c> or <c>update.own</c>).</summary>
    public bool CanChangeVisibility { get; init; }
}

/// <summary>The Settings screen: existing key/value settings (left) and an add/update form (right).</summary>
public sealed class SettingsIndexViewModel
{
    public string AppName { get; init; } = string.Empty;
    public IReadOnlyList<(string Key, string? Value)> Settings { get; init; } = Array.Empty<(string, string?)>();
}

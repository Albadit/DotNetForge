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

    /// <summary>The latest audit entries, newest first.</summary>
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

/// <summary>The Pages screen: the page tree (left) and the selected page's form (right).</summary>
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
    /// <summary>Every user (the "Add user" picker).</summary>
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

/// <summary>The Media screen: upload permission and the files (.docs/pages/media.md).</summary>
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

/// <summary>One content type on the Content types and Content Manager screens (.docs/pages/content-types.md).</summary>
public sealed record ContentTypeCard(
    Guid Id, string Name, string Slug, string? Description, string Icon, DotNetForge.Shared.Enums.ContentTypeKind Kind,
    int FieldCount, int EntryCount, int PublishedCount, DateTime UpdatedDate);

/// <summary>The Content types screen: every type and the templates a new one can start from.</summary>
public sealed class ContentTypesIndexViewModel
{
    public IReadOnlyList<ContentTypeCard> Types { get; init; } = Array.Empty<ContentTypeCard>();
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
}

/// <summary>The content type builder: the type's settings and fields (edited by content-types.js) plus what the field
/// settings need (the other types for relationships).</summary>
public sealed class ContentTypeEditViewModel
{
    public Guid Id { get; init; }
    public DotNetForge.Content.ContentTypeInput Input { get; init; } = new();
    public int EntryCount { get; init; }
    public IReadOnlyList<(Guid Id, string Name)> RelationTargets { get; init; } = Array.Empty<(Guid, string)>();
    public bool CanSave { get; init; }
    public bool CanDelete { get; init; }
}

/// <summary>The Content Manager overview: the types with their entry counts (.docs/pages/content-manager.md).</summary>
public sealed class ContentManagerViewModel
{
    public IReadOnlyList<ContentTypeCard> Types { get; init; } = Array.Empty<ContentTypeCard>();
    public bool CanManageTypes { get; init; }
    public bool CanCreateEntries { get; init; }
}

/// <summary>One row of an entry list: the entry and the text of the type's list columns.</summary>
public sealed record EntryRow(DotNetForge.Shared.Entities.ContentEntry Entry, IReadOnlyList<string> Columns, bool CanEdit, bool CanDelete);

/// <summary>The entry list of one content type: the current query and one page of rows.</summary>
public sealed class EntryListViewModel
{
    public DotNetForge.Shared.Entities.ContentType Type { get; init; } = new();
    public IReadOnlyList<(string Key, string Label)> Columns { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<EntryRow> Rows { get; init; } = Array.Empty<EntryRow>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageCount { get; init; }
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string Sort { get; init; } = "sortOrder";
    public bool Descending { get; init; }
    public bool CanCreate { get; init; }
    public bool CanPublish { get; init; }
    public bool CanDelete { get; init; }
    public bool CanReorder { get; init; }
    public bool CanEditType { get; init; }

    /// <summary>A list URL with this query, changed by <paramref name="change"/> (page resets unless set).</summary>
    public string Url(Action<Dictionary<string, string?>>? change = null)
    {
        var q = new Dictionary<string, string?>
        {
            ["q"] = Search,
            ["status"] = Status,
            ["sort"] = Sort == "sortOrder" ? null : Sort,
            ["desc"] = Descending ? "true" : null,
        };
        change?.Invoke(q);
        var query = string.Join("&", q.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value!)));
        return $"/admin/content/{Type.Slug}" + (query.Length > 0 ? "?" + query : string.Empty);
    }
}

/// <summary>A media file as the entry form's pickers show it.</summary>
public sealed record MediaChoice(Guid Id, string Name, string Url, bool IsImage);

/// <summary>An entry another entry can link to (relationship pickers).</summary>
public sealed record EntryChoice(Guid Id, string Title, bool Published);

/// <summary>
/// The entry editor: the form generated from the type's fields, the values to show (stored or posted), errors by field
/// path, and what the pickers offer.
/// </summary>
public sealed class EntryEditViewModel
{
    public DotNetForge.Shared.Entities.ContentType Type { get; init; } = new();
    public IReadOnlyList<DotNetForge.Core.Content.FieldDefinition> Fields { get; init; } = Array.Empty<DotNetForge.Core.Content.FieldDefinition>();
    public DotNetForge.Shared.Entities.ContentEntry? Entry { get; init; }
    public Newtonsoft.Json.Linq.JObject Values { get; init; } = new();
    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();
    public Guid? BaseVersion { get; init; }
    public bool Conflict { get; init; }

    /// <summary>Relationship options per related type id (the first ones and every selected one).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<EntryChoice>> Relations { get; init; } = new Dictionary<Guid, IReadOnlyList<EntryChoice>>();
    public IReadOnlyDictionary<Guid, string> RelationTypeNames { get; init; } = new Dictionary<Guid, string>();
    public IReadOnlyList<MediaChoice> Media { get; init; } = Array.Empty<MediaChoice>();
    public string? UpdatedBy { get; init; }
    public bool CanSave { get; init; }
    public bool CanPublish { get; init; }
    public bool CanDelete { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpload { get; init; }
    public bool IsNew => Entry is null;
}

/// <summary>One field of the entry form (the recursive <c>_Field</c> partial): its definition, value and form path.</summary>
public sealed record EntryFieldView(DotNetForge.Core.Content.FieldDefinition Field, Newtonsoft.Json.Linq.JToken? Value, string Path, EntryEditViewModel Form)
{
    /// <summary>The form field name (<c>f.</c> + the dotted path).</summary>
    public string Name => "f." + Path;

    /// <summary>An element id for the path.</summary>
    public string Id => "f-" + Path.Replace('.', '-');

    public string? Error => Form.Errors.TryGetValue(Path, out var e) ? e : null;
}

/// <summary>The API Endpoints list (.docs/pages/api-endpoints.md).</summary>
public sealed class ApiEndpointsIndexViewModel
{
    public IReadOnlyList<DotNetForge.Shared.Entities.CustomEndpoint> Endpoints { get; init; } = Array.Empty<DotNetForge.Shared.Entities.CustomEndpoint>();

    /// <summary>Each endpoint's operations (by endpoint id).</summary>
    public ILookup<Guid, DotNetForge.Shared.Entities.CustomEndpointOperation> Operations { get; init; } =
        Array.Empty<DotNetForge.Shared.Entities.CustomEndpointOperation>().ToLookup(o => o.EndpointId);

    /// <summary>False on MongoDB: endpoints can be kept but not run.</summary>
    public bool Supported { get; init; }
}

/// <summary>The endpoint editor: the endpoint (new when <see cref="Id"/> is null) with its operations (edited by endpoints.js).</summary>
public sealed class ApiEndpointEditViewModel
{
    public Guid? Id { get; init; }
    public DotNetForge.Content.Endpoints.CustomEndpointInput Input { get; init; } = new();
    public bool Supported { get; init; }

    /// <summary>The saved operations with their full path templates - what "Call it" shows.</summary>
    public IReadOnlyList<(DotNetForge.Shared.Entities.CustomEndpointOperation Operation, string Template)> Calls { get; init; } =
        Array.Empty<(DotNetForge.Shared.Entities.CustomEndpointOperation, string)>();

    /// <summary>The endpoint's own token (plain text), null for a new endpoint (created on its first save).</summary>
    public string? Token { get; init; }
}

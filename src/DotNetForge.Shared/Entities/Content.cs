using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A page in the tenant's content tree (.docs/features/content-pages-and-routing.md). Foundation subset: tree, slug,
/// SEO metadata, publish/menu flags, and page type.
/// </summary>
public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Lowercase, URL-safe segment. Unique within parent scope per tenant.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    public bool Published { get; set; }

    /// <summary>
    /// Legacy off switch, no longer set: <see cref="Published"/> is the only switch (off = disabled). Kept so pages
    /// saved with it stay hidden (<c>PageVisibility.Live</c> still checks it); saving a page clears it.
    /// </summary>
    public bool Disabled { get; set; }

    public bool DisplayInMenu { get; set; }

    public Guid? ParentPageId { get; set; }

    public int SortOrder { get; set; }

    public PageType PageType { get; set; } = PageType.Standard;

    /// <summary>Required when <see cref="PageType"/> is <see cref="PageType.UrlRedirect"/>.</summary>
    public string? TargetUrl { get; set; }

    /// <summary>Comma-separated SEO keywords.</summary>
    public string? SeoKeywords { get; set; }

    /// <summary>Canonical link for duplicate-content control (absolute URL).</summary>
    public string? CanonicalUrl { get; set; }

    /// <summary>Auto-publish time (UTC). When reached the page becomes published.</summary>
    public DateTime? ScheduledPublishDate { get; set; }

    /// <summary>Auto-unpublish time (UTC). Must be after <see cref="ScheduledPublishDate"/>.</summary>
    public DateTime? ScheduledUnpublishDate { get; set; }

    /// <summary>Reference to a File Manager asset; required when <see cref="PageType"/> is File.</summary>
    public string? FileReference { get; set; }

    /// <summary>Theme extension id for this page; null = the site's theme (.docs/features/themes.md).</summary>
    public string? ThemeId { get; set; }

    /// <summary>Asks search engines not to index the page (<c>robots: noindex</c>).</summary>
    public bool NoIndex { get; set; }

    /// <summary>Image shown when the page is shared (Open Graph <c>og:image</c>), absolute or site-relative URL.</summary>
    public string? SocialImageUrl { get; set; }

    /// <summary>
    /// The published page layout (page builder JSON, .docs/features/page-builder.md); null = the page has no layout and
    /// renders the default page view.
    /// </summary>
    public string? LayoutJson { get; set; }

    /// <summary>The page builder's unpublished draft; null when there are no unpublished changes.</summary>
    public string? DraftLayoutJson { get; set; }

    /// <summary>
    /// Changes on every draft save; an editor sends the version it loaded, so a save over someone else's newer draft
    /// is refused instead of silently overwriting it.
    /// </summary>
    public Guid? DraftVersion { get; set; }

    /// <summary>When the draft was last saved (UTC).</summary>
    public DateTime? DraftUpdatedDate { get; set; }

    /// <summary>Who saved the draft last (shown when a save conflicts).</summary>
    public Guid? DraftUpdatedById { get; set; }

    /// <summary>When the layout was last published (UTC).</summary>
    public DateTime? LayoutPublishedDate { get; set; }

    public Guid? CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A per-page grant to a role (.docs/features/content-pages-and-routing.md#page-permissions): <c>view</c> (when a page
/// has view grants, only those roles see it on the site) or <c>edit</c> (those roles may edit this page in the admin).
/// </summary>
public class PagePermission
{
    public const string View = "view";
    public const string Edit = "edit";

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid PageId { get; set; }

    public Guid RoleId { get; set; }

    /// <summary><see cref="View"/> or <see cref="Edit"/>.</summary>
    public string Permission { get; set; } = View;
}

/// <summary>
/// A per-page entry for one user (.docs/features/content-pages-and-routing.md#page-permissions): <see cref="Allow"/>
/// grants <c>view</c> or <c>edit</c> to that user; a block (<c>Allow = false</c>) takes it away, whatever their roles
/// grant (only Super Admins can't be blocked).
/// </summary>
public class PageUserPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid PageId { get; set; }

    public Guid UserId { get; set; }

    /// <summary><see cref="PagePermission.View"/> or <see cref="PagePermission.Edit"/>.</summary>
    public string Permission { get; set; } = PagePermission.View;

    /// <summary>True = allowed, false = blocked.</summary>
    public bool Allow { get; set; } = true;
}

/// <summary>An uploaded media asset (.docs/features/media-storage.md). Local storage is the default provider.</summary>
public class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public string RelativePath { get; set; } = string.Empty;

    public string FolderPath { get; set; } = "/";

    public bool IsPublic { get; set; } = true;

    public Guid? UploadedById { get; set; }

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;
}

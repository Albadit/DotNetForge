using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A page in the tenant's content tree (content_manager.md). Foundation subset: tree, slug,
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

    public Guid? CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>An uploaded media asset (file_manager.md). Local storage is the default provider.</summary>
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

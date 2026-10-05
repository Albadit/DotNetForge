using DotNetForge.Shared.Entities;

namespace DotNetForge.Shared.Content;

/// <summary>
/// The single owner of content-page rules (.docs/features/content-pages-and-routing.md): slug normalization,
/// parent/cycle checks, slug uniqueness per parent, one dynamic segment per parent, schedule ordering and page-type
/// requirements. Used by the admin Content Manager and the headless API so both enforce the same rules. Methods
/// validate and mutate tracked entities but never save - the caller saves and audits.
/// </summary>
public interface IPageService
{
    /// <summary>Validates <paramref name="input"/> and applies it to <paramref name="page"/> (new or existing).</summary>
    /// <returns>A user-facing error message, or <c>null</c> when the input was applied.</returns>
    Task<string?> ApplyAsync(Page page, PageInput input, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Validates and applies tree positions (drag-and-drop) for pages of the tenant.</summary>
    /// <returns>A user-facing error message, or <c>null</c> when the positions were applied.</returns>
    Task<string?> ReorderAsync(
        IReadOnlyList<PagePosition> positions, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Re-parents the page's children to its parent and removes the page, when that keeps the tree valid.</summary>
    /// <returns>A user-facing error message, or <c>null</c> when the page was removed.</returns>
    Task<string?> DeleteAsync(Page page, CancellationToken cancellationToken = default);
}

/// <summary>A page's position in the tree.</summary>
public sealed record PagePosition(Guid Id, Guid? ParentPageId, int SortOrder);

/// <summary>The editable fields of a content page, as posted by the Content Manager form or the API.</summary>
public sealed class PageInput
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? SeoKeywords { get; set; }
    public string? CanonicalUrl { get; set; }
    public bool Published { get; set; }
    public bool Disabled { get; set; }
    public bool DisplayInMenu { get; set; }
    public Guid? ParentPageId { get; set; }
    public int SortOrder { get; set; }
    public string PageType { get; set; } = "Standard";
    public string? TargetUrl { get; set; }
    public string? FileReference { get; set; }
    public DateTime? ScheduledPublishDate { get; set; }
    public DateTime? ScheduledUnpublishDate { get; set; }

    /// <summary>The editable fields of an existing page.</summary>
    public static PageInput From(Page page) => new()
    {
        Title = page.Title,
        Slug = page.Slug,
        MetaTitle = page.MetaTitle,
        MetaDescription = page.MetaDescription,
        SeoKeywords = page.SeoKeywords,
        CanonicalUrl = page.CanonicalUrl,
        Published = page.Published,
        Disabled = page.Disabled,
        DisplayInMenu = page.DisplayInMenu,
        ParentPageId = page.ParentPageId,
        SortOrder = page.SortOrder,
        PageType = page.PageType.ToString(),
        TargetUrl = page.TargetUrl,
        FileReference = page.FileReference,
        ScheduledPublishDate = page.ScheduledPublishDate,
        ScheduledUnpublishDate = page.ScheduledUnpublishDate,
    };
}

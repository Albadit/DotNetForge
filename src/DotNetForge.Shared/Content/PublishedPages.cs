using DotNetForge.Shared.Entities;

namespace DotNetForge.Shared.Content;

/// <summary>
/// The public "live" rule - the only place it is defined (.docs/features/content-pages-and-routing.md). A page is
/// publicly live only when published, not disabled, and within its scheduled window: the scheduled publish date has
/// been reached (or none set) and the scheduled unpublish date has not (or none set). Evaluated per request, so a
/// future "Scheduled publish" hides the page until that moment even though Published is checked.
/// </summary>
public static class PageVisibility
{
    public static IQueryable<Page> Live(this IQueryable<Page> pages, DateTime nowUtc) =>
        pages.Where(p => p.Published && !p.Disabled
            && (p.ScheduledPublishDate == null || p.ScheduledPublishDate <= nowUtc)
            && (p.ScheduledUnpublishDate == null || p.ScheduledUnpublishDate > nowUtc));
}

/// <summary>A live page reachable at a fixed public URL.</summary>
/// <param name="Path">The public URL path, e.g. <c>/about/team</c>; the root page is <c>/</c>.</param>
/// <param name="NoIndex">The page asks search engines not to index it (leave it out of sitemaps).</param>
public sealed record PublishedPage(
    Guid Id,
    Guid? ParentPageId,
    string Title,
    string Path,
    bool DisplayInMenu,
    int SortOrder,
    DateTime UpdatedDate,
    string? MetaDescription,
    bool NoIndex = false);

/// <summary>
/// The live pages of the public site with their URLs - for themes (menus) and extensions (sitemaps, search). Pages
/// below a page that is not live, dynamic <c>[param]</c> pages (which have no single URL) and pages restricted to some
/// roles (page view permissions) are left out.
/// </summary>
public interface IPublishedPages
{
    /// <summary>Ordered by tree position (parents before children, then <see cref="PublishedPage.SortOrder"/>).</summary>
    Task<IReadOnlyList<PublishedPage>> ListAsync(CancellationToken cancellationToken = default);
}

using DotNetForge.Data;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Default <see cref="IPageService"/>: every content-page rule lives here (.docs/features/content-pages-and-routing.md)
/// so the Content Manager, the drag-and-drop reorder and the headless API share one implementation.
/// <c>Published</c> is the author's intent; whether a page is actually live is derived from its schedule at render
/// time (see <c>HomeController</c>), so this never toggles it.
/// </summary>
public sealed class PageService : IPageService
{
    private readonly DotNetForgeDbContext _db;

    public PageService(DotNetForgeDbContext db)
    {
        _db = db;
    }

    public async Task<string?> ApplyAsync(Page page, PageInput input, Guid tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return "Title is required.";
        }

        if (FindTooLong(input) is { } tooLong)
        {
            return tooLong;
        }

        var slug = Slugify(input.Slug);
        if (slug.Length == 0)
        {
            return "A valid slug is required.";
        }

        if (!Enum.TryParse<PageType>(input.PageType, ignoreCase: true, out var pageType) ||
            !Enum.IsDefined(pageType))
        {
            return "Invalid page type.";
        }

        var tree = await LoadTreeAsync(tenantId, ct);

        if (input.ParentPageId is Guid parentId)
        {
            if (!tree.ContainsKey(parentId))
            {
                return "Parent page not found in this tenant.";
            }

            if (CreatesCycle(page.Id, parentId, id => tree.TryGetValue(id, out var n) ? n.ParentPageId : null))
            {
                return "That parent would create a cycle in the page tree.";
            }
        }

        var siblings = tree.Values.Where(p => p.ParentPageId == input.ParentPageId && p.Id != page.Id).ToList();
        if (siblings.Any(p => p.Slug == slug))
        {
            return $"A page with the slug '{slug}' already exists under this parent.";
        }

        // A parent can have at most one dynamic "[param]" route segment (otherwise resolution is ambiguous).
        if (IsDynamic(slug) && siblings.Any(p => IsDynamic(p.Slug)))
        {
            return "This parent already has a dynamic route ([…]); only one is allowed per parent.";
        }

        if (input.ScheduledPublishDate is DateTime sp && input.ScheduledUnpublishDate is DateTime su && su <= sp)
        {
            return "Scheduled unpublish must be after scheduled publish.";
        }

        if (pageType == PageType.UrlRedirect && string.IsNullOrWhiteSpace(input.TargetUrl))
        {
            return "Target URL is required for a URL redirect page.";
        }

        if (pageType == PageType.File && string.IsNullOrWhiteSpace(input.FileReference))
        {
            return "File reference is required for a File page.";
        }

        page.Title = input.Title.Trim();
        page.Slug = slug;
        page.MetaTitle = Trimmed(input.MetaTitle);
        page.MetaDescription = Trimmed(input.MetaDescription);
        page.SeoKeywords = Trimmed(input.SeoKeywords);
        page.CanonicalUrl = Trimmed(input.CanonicalUrl);
        page.Published = input.Published;
        page.Disabled = input.Disabled;
        page.DisplayInMenu = input.DisplayInMenu;
        page.ParentPageId = input.ParentPageId;
        page.SortOrder = input.SortOrder;
        page.PageType = pageType;
        page.TargetUrl = Trimmed(input.TargetUrl);
        page.FileReference = Trimmed(input.FileReference);
        page.ScheduledPublishDate = ToUtc(input.ScheduledPublishDate);
        page.ScheduledUnpublishDate = ToUtc(input.ScheduledUnpublishDate);
        return null;
    }

    public async Task<string?> ReorderAsync(IReadOnlyList<PagePosition> positions, Guid tenantId, CancellationToken ct = default)
    {
        if (positions.Count == 0)
        {
            return null;
        }

        var pages = await _db.Pages.Where(p => p.TenantId == tenantId).ToListAsync(ct);
        var byId = pages.ToDictionary(p => p.Id);

        // The tree after the move: current parents overlaid with the posted ones.
        var parentOf = pages.ToDictionary(p => p.Id, p => p.ParentPageId);
        foreach (var position in positions)
        {
            if (!byId.ContainsKey(position.Id))
            {
                return "One of the moved pages does not exist in this tenant.";
            }

            if (position.ParentPageId is Guid parent && !byId.ContainsKey(parent))
            {
                return "One of the target parents does not exist in this tenant.";
            }

            parentOf[position.Id] = position.ParentPageId;
        }

        foreach (var position in positions)
        {
            if (position.ParentPageId is Guid parent &&
                CreatesCycle(position.Id, parent, id => parentOf.TryGetValue(id, out var p) ? p : null))
            {
                return "That move would create a cycle in the page tree.";
            }
        }

        if (FindConflict(pages.Select(p => (p.Slug, Parent: parentOf[p.Id]))) is { } conflict)
        {
            return conflict;
        }

        var now = DateTime.UtcNow;
        foreach (var position in positions)
        {
            var page = byId[position.Id];
            if (page.ParentPageId != position.ParentPageId || page.SortOrder != position.SortOrder)
            {
                page.ParentPageId = position.ParentPageId;
                page.SortOrder = position.SortOrder;
                page.UpdatedDate = now;
            }
        }

        return null;
    }

    public async Task<string?> DeleteAsync(Page page, CancellationToken ct = default)
    {
        var pages = await _db.Pages.Where(p => p.TenantId == page.TenantId && p.Id != page.Id).ToListAsync(ct);
        var children = pages.Where(p => p.ParentPageId == page.Id).ToList();

        // Children move up to the deleted page's parent; that must not create duplicate slugs or a second
        // dynamic segment there.
        var afterDelete = pages.Select(p => (p.Slug, Parent: p.ParentPageId == page.Id ? page.ParentPageId : p.ParentPageId));
        if (FindConflict(afterDelete) is { } conflict)
        {
            return $"Can't delete: its child pages would move up a level and clash. {conflict}";
        }

        foreach (var child in children)
        {
            child.ParentPageId = page.ParentPageId;
            child.UpdatedDate = DateTime.UtcNow;
        }

        _db.Pages.Remove(page);
        return null;
    }

    /// <summary>
    /// Normalizes a scheduled date to UTC. Native <c>datetime-local</c> inputs post a zoneless wall-clock
    /// value (bound as <see cref="DateTimeKind.Unspecified"/>); the admin form labels these fields UTC, so
    /// an unspecified kind is taken as UTC. The schedule is compared against <c>DateTime.UtcNow</c> at render.
    /// </summary>
    public static DateTime? ToUtc(DateTime? value) => value is DateTime d
        ? d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        }
        : null;

    public static string Slugify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // "/" is the reserved slug for the site root (home) page.
        if (input.Trim() == "/")
        {
            return "/";
        }

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (ch is '-' or ' ' or '_')
            {
                sb.Append('-');
            }
            else if (ch is '[' or ']')
            {
                // Brackets mark a dynamic route segment, e.g. "[id]" matches any value.
                sb.Append(ch);
            }
        }

        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-');
    }

    public static bool IsDynamic(string slug) => slug.StartsWith('[') && slug.EndsWith(']');

    private async Task<Dictionary<Guid, (Guid Id, Guid? ParentPageId, string Slug)>> LoadTreeAsync(Guid tenantId, CancellationToken ct) =>
        (await _db.Pages.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => new { p.Id, p.ParentPageId, p.Slug })
            .ToListAsync(ct))
        .ToDictionary(p => p.Id, p => (p.Id, p.ParentPageId, p.Slug));

    /// <summary>Whether making <paramref name="parentId"/> the parent of <paramref name="pageId"/> creates a cycle.</summary>
    private static bool CreatesCycle(Guid pageId, Guid parentId, Func<Guid, Guid?> parentOf)
    {
        var visited = new HashSet<Guid>();
        Guid? cursor = parentId;
        while (cursor is Guid c)
        {
            if (c == pageId || !visited.Add(c))
            {
                return true;
            }

            cursor = parentOf(c);
        }

        return false;
    }

    /// <summary>Checks a whole tree for duplicate slugs or multiple dynamic segments under one parent.</summary>
    private static string? FindConflict(IEnumerable<(string Slug, Guid? Parent)> tree)
    {
        foreach (var group in tree.GroupBy(p => p.Parent))
        {
            var duplicate = group.GroupBy(p => p.Slug).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                return $"Two pages under the same parent would share the slug '{duplicate.Key}'.";
            }

            if (group.Count(p => IsDynamic(p.Slug)) > 1)
            {
                return "A parent would get more than one dynamic route ([…]); only one is allowed per parent.";
            }
        }

        return null;
    }

    /// <summary>Column limits from <c>DotNetForgeDbContext</c>, checked up front so input never fails at save time.</summary>
    private static string? FindTooLong(PageInput input)
    {
        (string Label, string? Value, int Max)[] fields =
        {
            ("Title", input.Title, 300), ("Slug", input.Slug, 200), ("Meta title", input.MetaTitle, 300),
            ("Meta description", input.MetaDescription, 1000), ("Keywords", input.SeoKeywords, 500),
            ("Canonical URL", input.CanonicalUrl, 2000), ("Target URL", input.TargetUrl, 2000),
            ("File reference", input.FileReference, 2000),
        };

        foreach (var (label, value, max) in fields)
        {
            if (value?.Trim().Length > max)
            {
                return $"{label} must be at most {max} characters.";
            }
        }

        return null;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

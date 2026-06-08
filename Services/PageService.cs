using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Validates and applies admin edits to a <see cref="Page"/> (content_manager.md). Centralizes the page
/// rules - slug normalization, parent-cycle and slug-uniqueness checks, the single dynamic "[param]" rule,
/// schedule ordering, and page-type conditionals - so the Razor content controller and any future caller
/// share one implementation. <c>Published</c> is the author's intent; whether a page is actually live is
/// derived from its schedule at render time (see <c>HomeController.Live</c>), so this never toggles it.
/// </summary>
public sealed class PageService
{
    private readonly DotNetForgeDbContext _db;

    public PageService(DotNetForgeDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Validates <paramref name="form"/> and, if valid, applies it to <paramref name="page"/> (which may be
    /// new or existing). Returns a human-readable error message, or <c>null</c> on success. Does not save.
    /// </summary>
    public async Task<string?> ApplyAsync(Page page, PageFormModel form, Guid tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(form.Title))
        {
            return "Title is required.";
        }

        var slug = Slugify(form.Slug);
        if (slug.Length == 0)
        {
            return "A valid slug is required.";
        }

        if (!Enum.TryParse<PageType>(form.PageType, ignoreCase: true, out var pageType))
        {
            return "Invalid page type.";
        }

        if (form.ParentPageId is Guid parentId)
        {
            var rows = await _db.Pages.AsNoTracking()
                .Where(p => p.TenantId == tenantId)
                .Select(p => new { p.Id, p.ParentPageId })
                .ToListAsync(ct);
            var parentOf = rows.ToDictionary(r => r.Id, r => r.ParentPageId);

            if (!parentOf.ContainsKey(parentId))
            {
                return "Parent page not found in this tenant.";
            }

            // Cycle check: walking the ancestors of the new parent must not reach this page.
            Guid? cursor = parentId;
            while (cursor is Guid c)
            {
                if (c == page.Id)
                {
                    return "That parent would create a cycle in the page tree.";
                }
                cursor = parentOf.TryGetValue(c, out var pp) ? pp : null;
            }
        }

        var slugTaken = await _db.Pages.AnyAsync(p =>
            p.TenantId == tenantId && p.ParentPageId == form.ParentPageId && p.Slug == slug && p.Id != page.Id, ct);
        if (slugTaken)
        {
            return $"A page with the slug '{slug}' already exists under this parent.";
        }

        // A parent can have at most one dynamic "[param]" route segment (otherwise resolution is ambiguous).
        if (slug.StartsWith('[') && slug.EndsWith(']'))
        {
            var siblingSlugs = await _db.Pages.AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.ParentPageId == form.ParentPageId && p.Id != page.Id)
                .Select(p => p.Slug)
                .ToListAsync(ct);
            if (siblingSlugs.Any(s => s.StartsWith('[') && s.EndsWith(']')))
            {
                return "This parent already has a dynamic route ([…]); only one is allowed per parent.";
            }
        }

        if (form.ScheduledPublishDate is DateTime sp && form.ScheduledUnpublishDate is DateTime su && su <= sp)
        {
            return "Scheduled unpublish must be after scheduled publish.";
        }

        if (pageType == PageType.UrlRedirect && string.IsNullOrWhiteSpace(form.TargetUrl))
        {
            return "Target URL is required for a URL redirect page.";
        }

        if (pageType == PageType.File && string.IsNullOrWhiteSpace(form.FileReference))
        {
            return "File reference is required for a File page.";
        }

        page.Title = form.Title.Trim();
        page.Slug = slug;
        page.MetaTitle = Trimmed(form.MetaTitle);
        page.MetaDescription = Trimmed(form.MetaDescription);
        page.SeoKeywords = Trimmed(form.SeoKeywords);
        page.CanonicalUrl = Trimmed(form.CanonicalUrl);
        page.Published = form.Published;
        page.Disabled = form.Disabled;
        page.DisplayInMenu = form.DisplayInMenu;
        page.ParentPageId = form.ParentPageId;
        page.SortOrder = form.SortOrder;
        page.PageType = pageType;
        page.TargetUrl = Trimmed(form.TargetUrl);
        page.FileReference = Trimmed(form.FileReference);
        page.ScheduledPublishDate = ToUtc(form.ScheduledPublishDate);
        page.ScheduledUnpublishDate = ToUtc(form.ScheduledUnpublishDate);
        return null;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    public static string Slugify(string input)
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
}

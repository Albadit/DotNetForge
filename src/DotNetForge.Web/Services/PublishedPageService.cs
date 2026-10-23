using DotNetForge.Data;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// <see cref="IPublishedPages"/> over the page table: one single-table query of the live pages, then the URL of each
/// page is built by walking up its parents in memory. Like the public routing in <c>HomeController</c> there is no
/// tenant resolution yet (.docs/features/multi-tenancy.md), so every tenant's live pages are included.
/// </summary>
public sealed class PublishedPageService : IPublishedPages
{
    private readonly DotNetForgeDbContext _db;

    public PublishedPageService(DotNetForgeDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PublishedPage>> ListAsync(CancellationToken cancellationToken = default)
    {
        var pages = await _db.Pages.AsNoTracking()
            .Live(DateTime.UtcNow)
            .Select(p => new { p.Id, p.ParentPageId, p.Slug, p.Title, p.DisplayInMenu, p.SortOrder, p.UpdatedDate, p.MetaDescription, p.NoIndex })
            .ToListAsync(cancellationToken);
        // Pages that allow view to some roles or users (and so everything below them) are for some visitors only: not listed.
        var restricted = (await _db.PagePermissions.AsNoTracking()
                .Where(p => p.Permission == PagePermission.View)
                .Select(p => p.PageId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        restricted.UnionWith(await _db.PageUserPermissions.AsNoTracking()
            .Where(p => p.Permission == PagePermission.View && p.Allow)
            .Select(p => p.PageId)
            .ToListAsync(cancellationToken));
        pages = pages.Where(p => !restricted.Contains(p.Id)).ToList();
        var byId = pages.ToDictionary(p => p.Id);
        var paths = new Dictionary<Guid, string?>();

        // null = not reachable at a fixed URL (a dynamic segment, or an ancestor that is not live).
        string? PathOf(Guid id)
        {
            if (paths.TryGetValue(id, out var known))
            {
                return known;
            }

            paths[id] = null; // guards against a parent cycle in bad data
            var page = byId[id];
            string? path;
            if (page.Slug.StartsWith('[') && page.Slug.EndsWith(']'))
            {
                path = null;
            }
            else if (page.ParentPageId is null)
            {
                path = page.Slug is "/" or "home" ? "/" : "/" + page.Slug;
            }
            else
            {
                var parent = byId.ContainsKey(page.ParentPageId.Value) ? PathOf(page.ParentPageId.Value) : null;
                path = parent is null ? null : parent == "/" ? "/" + page.Slug : $"{parent}/{page.Slug}";
            }

            paths[id] = path;
            return path;
        }

        return pages
            .Select(p => (Page: p, Path: PathOf(p.Id)))
            .Where(x => x.Path is not null)
            .OrderBy(x => x.Path!.Count(c => c == '/'))
            .ThenBy(x => x.Page.SortOrder)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => new PublishedPage(x.Page.Id, x.Page.ParentPageId, x.Page.Title, x.Path!, x.Page.DisplayInMenu,
                x.Page.SortOrder, x.Page.UpdatedDate, x.Page.MetaDescription, x.Page.NoIndex))
            .ToList();
    }
}

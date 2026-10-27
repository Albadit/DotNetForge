using DotNetForge.Data;
using DotNetForge.Extensions;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Picks the theme of the public site (.docs/features/themes.md): a page's own theme when it has one that is enabled,
/// else the theme a parent page hands down to its children (<see cref="Page.ThemeForChildren"/>), else
/// <see cref="DefaultThemeId"/> when that theme is enabled, otherwise the first enabled theme by name, otherwise none
/// (the built-in views). The admin area, setup and sign-in never use a theme.
/// </summary>
public sealed class ThemeResolver
{
    /// <summary>The theme the site prefers when a page names none: the bundled default theme.</summary>
    public const string DefaultThemeId = "dotnetforge.theme.default";

    private readonly ExtensionCatalog _catalog;
    private readonly DotNetForgeDbContext _db;

    public ThemeResolver(ExtensionCatalog catalog, DotNetForgeDbContext db)
    {
        _catalog = catalog;
        _db = db;
    }

    /// <param name="pageThemeId">The theme a content page asks for (<see cref="PageThemeIdAsync"/>); used when that theme is enabled.</param>
    public async Task<ActiveExtension?> GetActiveThemeAsync(CancellationToken cancellationToken = default, string? pageThemeId = null)
    {
        var themes = await _catalog.GetActiveAsync("theme", cancellationToken);
        if (themes.Count == 0)
        {
            return null;
        }

        if (pageThemeId is not null && themes.FirstOrDefault(t => string.Equals(t.Id, pageThemeId, StringComparison.OrdinalIgnoreCase)) is { } own)
        {
            return own;
        }

        return themes.FirstOrDefault(t => string.Equals(t.Id, DefaultThemeId, StringComparison.OrdinalIgnoreCase)) ?? themes[0];
    }

    /// <summary>The theme a page asks for: its own, else the one the nearest parent hands down, else null (the site theme).</summary>
    public async Task<string?> PageThemeIdAsync(Page page, CancellationToken cancellationToken = default)
    {
        if (page.ThemeId is not null || page.ParentPageId is null)
        {
            return page.ThemeId;
        }

        // One single-table query of the tree's theme settings; the walk up happens in memory.
        var tree = await _db.Pages.AsNoTracking()
            .Select(p => new ThemeNode(p.Id, p.ParentPageId, p.Title, p.ThemeId, p.ThemeForChildren))
            .ToListAsync(cancellationToken);
        return InheritedFrom(tree, page.ParentPageId)?.ThemeId;
    }

    /// <summary>The nearest page from <paramref name="parentId"/> upwards that hands its theme down, or null.</summary>
    public static ThemeNode? InheritedFrom(IReadOnlyCollection<ThemeNode> tree, Guid? parentId)
    {
        var byId = tree.ToDictionary(n => n.Id);
        var seen = new HashSet<Guid>();
        while (parentId is { } id && seen.Add(id) && byId.TryGetValue(id, out var node))
        {
            if (node.ThemeId is not null)
            {
                // A parent with its own theme ends the walk: it either hands that theme down or keeps it to itself.
                return node.ThemeForChildren ? node : null;
            }

            parentId = node.ParentPageId;
        }

        return null;
    }
}

/// <summary>A page's place in the tree and its theme settings - what theme inheritance needs.</summary>
public sealed record ThemeNode(Guid Id, Guid? ParentPageId, string Title, string? ThemeId, bool ThemeForChildren);

using DotNetForge.Data;
using DotNetForge.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Picks the theme of the public site (.docs/features/themes.md): a page's own theme when it has one that is enabled,
/// else the default tenant's <c>DefaultTheme</c> when that theme is enabled, otherwise the first enabled theme by name,
/// otherwise none (the built-in views). The admin area,
/// setup and sign-in never use a theme.
/// </summary>
public sealed class ThemeResolver
{
    private readonly DotNetForgeDbContext _db;
    private readonly ExtensionCatalog _catalog;

    public ThemeResolver(DotNetForgeDbContext db, ExtensionCatalog catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    /// <param name="pageThemeId">A content page's own theme (<c>Page.ThemeId</c>); used when that theme is enabled.</param>
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

        // No tenant resolution yet (.docs/features/multi-tenancy.md): the public site belongs to the default tenant.
        var preferred = await _db.Tenants.AsNoTracking()
            .OrderBy(t => t.CreatedDate)
            .Select(t => t.DefaultTheme)
            .FirstOrDefaultAsync(cancellationToken);
        return themes.FirstOrDefault(t => string.Equals(t.Id, preferred, StringComparison.OrdinalIgnoreCase)) ?? themes[0];
    }
}

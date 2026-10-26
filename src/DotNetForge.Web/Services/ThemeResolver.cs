using DotNetForge.Extensions;

namespace DotNetForge.Web.Services;

/// <summary>
/// Picks the theme of the public site (.docs/features/themes.md): a page's own theme when it has one that is enabled,
/// else <see cref="DefaultThemeId"/> when that theme is enabled, otherwise the first enabled theme by name,
/// otherwise none (the built-in views). The admin area,
/// setup and sign-in never use a theme.
/// </summary>
public sealed class ThemeResolver
{
    /// <summary>The theme the site prefers when a page names none: the bundled default theme.</summary>
    public const string DefaultThemeId = "dotnetforge.theme.default";

    private readonly ExtensionCatalog _catalog;

    public ThemeResolver(ExtensionCatalog catalog)
    {
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

        return themes.FirstOrDefault(t => string.Equals(t.Id, DefaultThemeId, StringComparison.OrdinalIgnoreCase)) ?? themes[0];
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotNetForge.Web.Areas.Admin.Navigation;

/// <summary>
/// The admin shell around a screen (.docs/architecture/pages.md#navigation): what <c>_AdminLayout</c> loads, and the
/// screen-only response that <c>navigation.js</c> asks for when it swaps a screen in place.
/// </summary>
public static class AdminShell
{
    /// <summary>Sent by navigation.js: answer with the screen only (top bar, content, toasts, the screen's scripts).</summary>
    public const string ScreenHeader = "X-Dnf-Screen";

    public static readonly string[] Stylesheets =
    {
        "/_content/DotNetForge.UI/dnf.css", "/css/admin/shell.css", "/css/admin/content.css", "/css/admin/media.css",
        "/css/admin/tools.css", "/css/admin/people.css",
    };

    public static readonly string[] Scripts =
    {
        "/_content/DotNetForge.UI/dnf-theme.js", "/_content/DotNetForge.UI/dnf.js", "/js/admin/nav-state.js", "/js/admin/navigation.js",
    };

    /// <summary>Where nav-state.js keeps the sidebar groups the user opened or closed: {"label": true|false}.</summary>
    public const string GroupsCookie = "dnf-nav";

    private static readonly IReadOnlyDictionary<string, bool> NoGroups = new Dictionary<string, bool>();

    public static bool IsScreenOnly(HttpRequest request) => request.Headers[ScreenHeader] == "1";

    /// <summary>
    /// The sidebar groups the user opened (true) or closed (false), so a page arrives with them that way and nothing
    /// moves when nav-state.js runs. A display preference the browser writes: anything unreadable counts as none.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> RememberedGroups(HttpRequest request)
    {
        if (request.Cookies[GroupsCookie] is not { Length: > 0 and <= 4096 } value)
        {
            return NoGroups;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(value) ?? NoGroups;
        }
        catch (JsonException)
        {
            return NoGroups;
        }
    }

    /// <summary>
    /// What a page already showing the shell must have for a screen to be swapped into it: the same files (by their
    /// versioned URLs), the same sidebar and the same account. Anything else and navigation.js loads the page instead.
    /// </summary>
    public static string Fingerprint(IEnumerable<string> assetUrls, AdminNavModel nav, string account)
    {
        var parts = new StringBuilder();
        foreach (var url in assetUrls)
        {
            parts.Append(url).Append('\n');
        }

        foreach (var section in nav.Sections)
        {
            parts.Append(section.Label).Append('\n');
            foreach (var link in section.Links.Concat(section.Groups.SelectMany(g => g.Links.Select(l => l with { Label = g.Label + "/" + l.Label }))))
            {
                parts.Append(link.Label).Append('|').Append(link.Href).Append('|').Append(link.Icon).Append('|').Append(link.Planned).Append('\n');
            }
        }

        parts.Append(account);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString())))[..16].ToLowerInvariant();
    }

    /// <summary>The sidebar link of the current screen, if it has one.</summary>
    public static string? ActiveHref(AdminNavModel nav) =>
        nav.Sections.SelectMany(s => s.Links.Concat(s.Groups.SelectMany(g => g.Links))).FirstOrDefault(l => l.Active)?.Href;
}

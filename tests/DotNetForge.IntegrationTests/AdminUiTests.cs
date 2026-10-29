using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Navigation;
using DotNetForge.Web.Ui;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The Forge UI (.docs/architecture/design-system.md): every screen honours the CSP and the page structure, the
/// sidebar only offers what the user may open, the media bulk action keeps per-file permissions, and every icon the
/// UI asks for exists in the sprite.
/// </summary>
public sealed partial class AdminUiTests
{
    private static readonly string[] AdminScreens =
    {
        "/admin", "/admin/pages", "/admin/content", "/admin/content-types", "/admin/content-types/new", "/admin/media", "/admin/settings", "/admin/users", "/admin/users/create", "/admin/roles",
        "/admin/roles/create", "/admin/audit-logs", "/admin/profile",
        "/admin/api-tokens", "/admin/api-tokens/create", "/admin/plugins", "/admin/marketplace", "/admin/content-history",
        "/admin/internationalization", "/admin/data", "/admin/webhooks", "/admin/email/configuration",
        "/admin/email/templates", "/admin/providers", "/admin/advanced-settings",
    };

    [GeneratedRegex(@"\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyle();

    [GeneratedRegex(@"<style[\s>]|<script(?![^>]*\ssrc=)[^>]*>|\son[a-z]+\s*=\s*[""']", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptOrStyleBlock();

    [GeneratedRegex(@"<h1[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex H1();

    /// <summary>The CSP forbids inline script and style; every screen has one h1, a language and a skip link.</summary>
    internal static void AssertWellFormed(string path, string html)
    {
        var style = InlineStyle().Match(html);
        Assert.False(style.Success, $"{path} has an inline style attribute: {Around(html, style.Index)}");
        var block = InlineScriptOrStyleBlock().Match(html);
        Assert.False(block.Success, $"{path} has an inline script, style block or event handler: {Around(html, block.Index)}");
        Assert.True(H1().Matches(html).Count == 1, $"{path} must have exactly one <h1>.");
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("class=\"skip-link\" href=\"#main\"", html);
    }

    private static string Around(string html, int index) =>
        html.Substring(Math.Max(0, index - 120), Math.Min(240, html.Length - Math.Max(0, index - 120)));

    [Fact]
    public async Task Every_screen_renders_without_inline_script_or_style()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        Guid pageId = default, userId = default;
        List<Guid> roleIds = new();
        await factory.WithDbAsync(async db =>
        {
            pageId = await db.Pages.Select(p => p.Id).FirstAsync();
            userId = await db.Users.Select(u => u.Id).FirstAsync();
            roleIds = await db.Roles.Select(r => r.Id).ToListAsync();
        });

        var editors = roleIds.Select(id => $"/admin/roles/{id}").Append($"/admin/users/{userId}").Append($"/admin/pages?selected={pageId}");
        foreach (var path in AdminScreens.Concat(editors))
        {
            var response = await admin.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}.");
            AssertWellFormed(path, await response.Content.ReadAsStringAsync());
        }

        // The access-denied screen answers 403 on purpose; its markup follows the same rules.
        AssertWellFormed("/account/denied", await (await admin.GetAsync("/account/denied")).Content.ReadAsStringAsync());

        var visitor = factory.CreateNoRedirectClient();
        foreach (var path in new[] { "/login", "/", "/about", "/error" })
        {
            AssertWellFormed(path, await visitor.GetStringAsync(path));
        }
    }

    [Fact]
    public async Task The_sidebar_shows_only_screens_the_role_may_open()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");
        var admin = await factory.SignInAsync();

        var editorHtml = await editor.GetStringAsync("/admin");
        Assert.Contains("href=\"/admin/content\"", editorHtml);
        Assert.Contains("href=\"/admin/media\"", editorHtml);
        foreach (var restricted in new[] { "/admin/plugins", "/admin/users", "/admin/settings", "/admin/api-tokens", "/admin/content-types" })
        {
            Assert.DoesNotContain($"href=\"{restricted}\"", editorHtml);
            // Hiding is not the gate: the screen itself still refuses.
            var direct = await editor.GetAsync(restricted);
            Assert.Contains("/account/denied", direct.Headers.Location?.OriginalString);
        }

        var adminHtml = await admin.GetStringAsync("/admin/api-tokens/create");
        Assert.Contains("href=\"/admin/plugins\"", adminHtml);
        // A child route marks its screen active and puts it in the breadcrumb trail.
        Assert.Contains("href=\"/admin/api-tokens\" aria-current=\"page\"", adminHtml);
        Assert.Contains("<span aria-current=\"page\">Create API token</span>", adminHtml);

        // Only the group holding the active screen is marked active. nav-state.js keeps the others as the user left
        // them; it loads synchronously right after the sidebar so that state is in place before the first paint.
        Assert.Matches("data-nav-group=\"API &amp; Webhooks\" open=\"open\"\\s+data-nav-active=\"true\"", adminHtml);
        Assert.Matches("data-nav-group=\"Global Settings\"\\s+data-nav-active=\"false\"", adminHtml);
        Assert.Matches("data-nav-group=\"Email\"\\s+data-nav-active=\"false\"", adminHtml);
        Assert.Matches("</aside>\\s*<script src=\"/js/forge/nav-state.js\\?v=[^\"]+\"></script>", adminHtml);

        // Pages comes before the Content Manager in the Main section.
        Assert.True(adminHtml.IndexOf("href=\"/admin/pages\"", StringComparison.Ordinal)
            < adminHtml.IndexOf("href=\"/admin/content\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bulk_delete_removes_the_selected_files_the_user_may_delete()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        foreach (var name in new[] { "a.png", "b.png", "c.png" })
        {
            await MediaTests.UploadAsync(admin, name, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, isPublic: true);
        }

        List<Guid> ids = new();
        await factory.WithDbAsync(async db => ids = await db.MediaFiles.OrderBy(m => m.FileName).Select(m => m.Id).ToListAsync());

        // An Author may only delete their own uploads: nothing of the admin's goes.
        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");
        await PostIdsAsync(author, ids);
        await factory.WithDbAsync(async db => Assert.Equal(3, await db.MediaFiles.CountAsync()));

        var response = await PostIdsAsync(admin, ids.Take(2));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal(ids[2], (await db.MediaFiles.SingleAsync()).Id));
        Assert.Contains("Deleted 2 items.", await admin.GetStringAsync("/admin/media"));
        await factory.WithDbAsync(async db => Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.MediaDeleted)));
    }

    private static async Task<HttpResponseMessage> PostIdsAsync(HttpClient client, IEnumerable<Guid> ids)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/admin/media");
        var fields = ids.Select(id => new KeyValuePair<string, string>("ids", id.ToString()))
            .Append(new("__RequestVerificationToken", token));
        return await client.PostAsync("/admin/media/delete-selected", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task The_dashboard_shows_recent_activity_and_offers_only_permitted_actions()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");

        var adminHtml = await admin.GetStringAsync("/admin");
        Assert.Contains("Signed in", adminHtml); // the admin's own sign-in, from the audit log
        Assert.Contains("New API token", adminHtml);
        Assert.DoesNotContain("New API token", await editor.GetStringAsync("/admin"));
    }

    [Fact]
    public void Every_icon_the_ui_uses_exists_in_the_sprite()
    {
        var root = AppPaths.FindRepositoryRoot(AppContext.BaseDirectory)!;
        var sprite = File.ReadAllText(Path.Combine(root, "src", "DotNetForge.Web", "wwwroot", "img", "icons.svg"));
        var symbols = Regex.Matches(sprite, "<symbol id=\"([a-z0-9-]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        var used = new HashSet<string>();
        var views = Directory.EnumerateFiles(Path.Combine(root, "src", "DotNetForge.Web"), "*.cshtml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "extensions"), "*.cshtml", SearchOption.AllDirectories));
        foreach (var view in views)
        {
            // Literal names only: <icon name="x">, icon="x" on components, data-… toast icons are covered by C# below.
            foreach (Match m in Regex.Matches(File.ReadAllText(view), "(?:<icon[^>]*\\sname|\\sicon)=\"([a-z0-9-]+)\""))
            {
                used.Add(m.Groups[1].Value);
            }
        }

        used.UnionWith(AdminNavigation.Tree.SelectMany(s => s.Links.Concat(s.Groups.SelectMany(g => g.Links))).Select(l => l.Icon));
        used.UnionWith(new[] { "panel", "circle-check", "circle-x", "info", "triangle-alert", "sort" }); // C#/JS-built icons
        used.UnionWith(typeof(AuditActions).GetFields().Select(f => AuditPresentation.Icon((string)f.GetValue(null)!)));
        foreach (var kind in new[] { "image/png", "video/mp4", "audio/mpeg", "application/zip", "application/pdf" })
        {
            used.Add(MediaPresentation.Classify(kind).Icon);
        }

        var missing = used.Where(name => !symbols.Contains(name)).Order().ToList();
        Assert.True(missing.Count == 0, $"Icons missing from icons.svg: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(30 * 3600, "yesterday")]
    [InlineData(4 * 86400, "4 days ago")]
    [InlineData(-5 * 60, "in 5 min")]
    public void Timestamps_read_relative_to_now(int secondsAgo, string expected)
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, TimestampTagHelper.Relative(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void Old_timestamps_show_the_date()
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("3 Oct 2026", TimestampTagHelper.Relative(new DateTime(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc), now));
    }
}

using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Web.Areas.Admin.Navigation;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The screen-only response (.docs/architecture/pages.md#navigation): asked for by navigation.js, an admin screen comes
/// without what the page it is swapped into already has, and names the shell it fits.
/// </summary>
public sealed partial class ScreenNavigationTests
{
    [Fact]
    public async Task A_screen_comes_alone_and_names_the_shell_it_fits()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var client = await factory.SignInAsync();

        var full = await client.GetStringAsync("/admin/users");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
        request.Headers.Add(AdminShell.ScreenHeader, "1");
        var response = await client.SendAsync(request);
        var screen = await response.Content.ReadAsStringAsync();

        // The screen: top bar, content, toasts.
        Assert.Contains(AdminShell.ScreenHeader, response.Headers.Vary);
        Assert.Contains("class=\"dnf-shell__main\"", screen);
        Assert.Contains("<h1 class=\"dnf-page-header__title\">Users</h1>", screen);
        Assert.Contains("data-toasts", screen);

        // Not what the page already has.
        Assert.DoesNotContain("class=\"dnf-sidebar\"", screen);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", screen);
        Assert.DoesNotContain("dnf-confirm-dialog", screen);
        Assert.DoesNotContain("navigation.js", screen);
        Assert.True(screen.Length < full.Length);

        // The same shell as the full page and as another screen; the active link for the sidebar.
        var shell = Meta(full, "dnf-shell");
        Assert.Matches("^[0-9a-f]{16}$", shell);
        Assert.Equal(shell, Meta(screen, "dnf-shell"));
        Assert.Equal(shell, Meta(await client.GetStringAsync("/admin/roles"), "dnf-shell"));
        Assert.Equal("/admin/users", Meta(screen, "dnf-active"));
    }

    /// <summary>
    /// Asked for again with its ETag, an unchanged screen comes as 304 without a body - although its antiforgery tokens
    /// are new - and a changed one comes whole. A full page never takes part.
    /// </summary>
    [Fact]
    public async Task An_unchanged_screen_asked_for_again_comes_without_a_body()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var client = await factory.SignInAsync();

        async Task<HttpResponseMessage> ScreenAsync(string? etag)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/roles");
            request.Headers.Add(AdminShell.ScreenHeader, "1");
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            return await client.SendAsync(request);
        }

        var first = await ScreenAsync(null);
        var etag = first.Headers.ETag!.ToString();
        var firstHtml = await first.Content.ReadAsStringAsync();
        Assert.Contains("__RequestVerificationToken", firstHtml);

        var again = await ScreenAsync(etag);
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Empty(await again.Content.ReadAsByteArrayAsync());
        Assert.Equal(etag, again.Headers.ETag!.ToString());

        var full = await client.GetAsync("/admin/roles");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Null(full.Headers.ETag);

        var created = await DotNetForgeWebFactory.PostFormAsync(client, "/admin/roles/create", "/admin/roles/create",
            new Dictionary<string, string> { ["Name"] = "Reviewers", ["Description"] = "Reads drafts." });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        await client.GetAsync(created.Headers.Location); // the toast is shown once, on the screen it was meant for
        var changed = await ScreenAsync(etag);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(etag, changed.Headers.ETag!.ToString());
        Assert.Contains("Reviewers", await changed.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A full page arrives with the sidebar groups as the user left them (the dnf-nav cookie nav-state.js keeps), so
    /// nothing moves when the script runs; the group holding the screen is always open, and an unreadable cookie counts
    /// as none.
    /// </summary>
    [Fact]
    public async Task The_sidebar_arrives_with_its_groups_as_the_user_left_them()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var client = await factory.SignInAsync();

        async Task<string> UsersAsync(string groups)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
            request.Headers.Add("Cookie", AdminShell.GroupsCookie + "=" + Uri.EscapeDataString(groups));
            return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        }

        var page = await UsersAsync("""{"API & Webhooks":true,"Email":false,"Administration Panel":false}""");
        Assert.True(Open(page, "API & Webhooks"));
        Assert.False(Open(page, "Email"));
        Assert.True(Open(page, "Administration Panel")); // holds Users

        var unreadable = await UsersAsync("{\"API & Webhooks\":");
        Assert.False(Open(unreadable, "API & Webhooks"));
        Assert.True(Open(unreadable, "Administration Panel"));
    }

    private static bool Open(string html, string group) =>
        Regex.IsMatch(html, $"data-nav-group=\"{Regex.Escape(WebUtility.HtmlEncode(group))}\" open=\"open\"");

    private static string Meta(string html, string name) =>
        Regex.Match(html, $"<meta name=\"{name}\" content=\"([^\"]*)\"").Groups[1].Value;
}

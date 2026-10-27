using DotNetForge.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The public site without a theme extension (.docs/pages/public-page.md) - the page menu, the Admin link only for
/// admin users - and theme inheritance (.docs/features/themes.md): a page's theme handed down to its child pages.
/// </summary>
public sealed class PublicSiteTests
{
    [Fact]
    public async Task The_built_in_header_lists_the_menu_pages_and_shows_admin_only_to_admin_users()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();

        // Seeded: Home, About, News published and in the menu; Contact in the menu but not published.
        var visitor = await factory.CreateNoRedirectClient().GetStringAsync("/about");
        Assert.Contains("class=\"site-nav\"", visitor);
        Assert.Contains("href=\"/news\"", visitor);
        Assert.Contains("href=\"/about\" aria-current=\"page\"", visitor);
        Assert.DoesNotContain("href=\"/contact\"", visitor);
        Assert.DoesNotContain("href=\"/admin\"", visitor);

        var admin = await factory.SignInAsync();
        Assert.Contains("href=\"/admin\"", await admin.GetStringAsync("/about"));
    }

    [Fact]
    public async Task A_page_hands_its_theme_down_to_child_pages_without_their_own()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        // About picks a theme and hands it down; Team (its child) has none of its own.
        Guid aboutId = default, teamId = default, historyId = default;
        await factory.WithDbAsync(async db =>
        {
            aboutId = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync();
            teamId = await db.Pages.Where(p => p.Slug == "team").Select(p => p.Id).SingleAsync();
            historyId = await db.Pages.Where(p => p.Slug == "history").Select(p => p.Id).SingleAsync();
        });
        var saved = await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/pages?selected={aboutId}", $"/admin/pages/update/{aboutId}", new Dictionary<string, string>
        {
            ["Title"] = "About",
            ["Slug"] = "about",
            ["Published"] = "true",
            ["DisplayInMenu"] = "true",
            ["PageType"] = "Standard",
            ["ThemeId"] = "dotnetforge.theme.default",
            ["ThemeForChildren"] = "true",
        });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, saved.StatusCode);

        await factory.WithDbAsync(async db =>
        {
            var resolver = new ThemeResolver(factory.Services.GetRequiredService<DotNetForge.Extensions.ExtensionCatalog>(), db);
            var about = await db.Pages.SingleAsync(p => p.Id == aboutId);
            Assert.True(about.ThemeForChildren);
            Assert.Equal("dotnetforge.theme.default", await resolver.PageThemeIdAsync(await db.Pages.SingleAsync(p => p.Id == teamId)));

            // A child with its own theme keeps it - and stops the hand-down for its own children unless it hands down too.
            var history = await db.Pages.SingleAsync(p => p.Id == historyId);
            history.ThemeId = "tests.theme.other";
            await db.SaveChangesAsync();
            Assert.Equal("tests.theme.other", await resolver.PageThemeIdAsync(history));
        });

        // The editor of a child page says where its theme comes from.
        Assert.Contains("Parent's theme", await admin.GetStringAsync($"/admin/pages?selected={teamId}"));
    }

    [Fact]
    public void Theme_inheritance_stops_at_the_next_page_with_a_theme_of_its_own()
    {
        var root = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var leaf = Guid.NewGuid();
        var tree = new[]
        {
            new ThemeNode(root, null, "Root", "theme.a", ThemeForChildren: true),
            new ThemeNode(middle, root, "Middle", null, ThemeForChildren: false),
            new ThemeNode(leaf, middle, "Leaf", null, ThemeForChildren: false),
        };
        Assert.Equal("Root", ThemeResolver.InheritedFrom(tree, middle)?.Title);
        Assert.Equal("Root", ThemeResolver.InheritedFrom(tree, root)?.Title);

        // Middle picks its own theme without handing it down: the leaf gets the site theme, not Root's.
        tree[1] = tree[1] with { ThemeId = "theme.b" };
        Assert.Null(ThemeResolver.InheritedFrom(tree, middle));
        tree[1] = tree[1] with { ThemeForChildren = true };
        Assert.Equal("Middle", ThemeResolver.InheritedFrom(tree, middle)?.Title);
        Assert.Null(ThemeResolver.InheritedFrom(tree, null));
    }
}

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// End-to-end checks over the real host: install detection, the setup gate, admin auth gating, and
/// API token enforcement (.docs/features/installation.md, .docs/architecture/pages.md, .docs/features/headless-api.md).
/// </summary>
public sealed class CmsIntegrationTests
{
    private static HttpClient NoRedirectClient(DotNetForgeWebFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Health_endpoint_is_ok_even_before_install()
    {
        using var factory = new DotNetForgeWebFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Before_install_all_requests_redirect_to_setup()
    {
        using var factory = new DotNetForgeWebFactory();
        using var client = NoRedirectClient(factory);

        var root = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/setup", root.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_switched_off_home_page_is_not_found_and_the_list_is_only_for_a_site_without_one()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            foreach (var p in await db.Pages.Where(p => p.Slug == "/" || p.Slug == "home").ToListAsync()) p.Published = false;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            db.Pages.RemoveRange(await db.Pages.Where(p => p.Slug == "/" || p.Slug == "home").ToListAsync());
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task New_pages_get_short_unique_slugs_and_the_tree_offers_delete()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", "/admin/pages/create", new Dictionary<string, string>());
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", "/admin/pages/create", new Dictionary<string, string>());

        Guid second = default;
        await factory.WithDbAsync(async db =>
        {
            var slugs = await db.Pages.Where(p => p.Slug.StartsWith("new-page")).OrderBy(p => p.SortOrder).Select(p => p.Slug).ToListAsync();
            Assert.Equal(new[] { "new-page", "new-page-2" }, slugs);
            second = await db.Pages.Where(p => p.Slug == "new-page-2").Select(p => p.Id).SingleAsync();
        });
        var tree = await admin.GetStringAsync("/admin/pages");
        Assert.Contains($"action=\"/admin/pages/delete/{second}\"", tree);
        Assert.Contains($"action=\"/admin/pages/duplicate/{second}\"", tree);
        Assert.Contains("data-tree-toggle", tree);
        Assert.Contains("data-tree-collapse-all", tree);
        // "View on site": a live page links to its public URL, a switched-off one says why it can't be opened.
        Assert.Matches("<a href=\"/about\"[^>]*target=\"_blank\"", tree);
        Assert.Contains("Not on the site: this page is disabled.", tree);

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/delete/{second}", new Dictionary<string, string>());
        await factory.WithDbAsync(async db => Assert.False(await db.Pages.AnyAsync(p => p.Id == second)));
    }

    [Fact]
    public async Task Duplicate_copies_a_page_below_the_original_switched_off()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        Guid aboutId = default;
        int aboutOrder = 0;
        await factory.WithDbAsync(async db =>
        {
            var about = await db.Pages.SingleAsync(p => p.Slug == "about");
            about.MetaDescription = "About us";
            about.LayoutJson = "{}";
            await db.SaveChangesAsync();
            (aboutId, aboutOrder) = (about.Id, about.SortOrder);
        });

        var response = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/duplicate/{aboutId}", new Dictionary<string, string>());
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/duplicate/{aboutId}", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var copies = await db.Pages.Where(p => p.Slug.StartsWith("about-copy")).OrderBy(p => p.Slug).ToListAsync();
            Assert.Equal(new[] { "about-copy", "about-copy-2" }, copies.Select(p => p.Slug));
            var copy = copies[0];
            Assert.Equal("About (copy)", copy.Title);
            Assert.False(copy.Published);
            Assert.Equal("About us", copy.MetaDescription);
            Assert.Equal("{}", copy.LayoutJson);
            Assert.Equal((await db.Pages.SingleAsync(p => p.Id == aboutId)).ParentPageId, copy.ParentPageId);
            // Both copies sit right after the original (the newest first); later siblings moved down.
            Assert.Equal(aboutOrder + 1, copies[1].SortOrder);
            Assert.Equal(aboutOrder + 2, copy.SortOrder);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == DotNetForge.Shared.Constants.AuditActions.ContentCreated && a.EntityId == copy.Id.ToString()));
        });
    }

    [Fact]
    public async Task Setup_page_is_served_before_install()
    {
        // The setup wizard is a server-rendered Razor page reachable before install.
        using var factory = new DotNetForgeWebFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/setup");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email", html);
    }

    [Fact]
    public async Task After_install_setup_is_blocked()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task After_install_admin_requires_authentication()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        // The admin is server-rendered Razor; an unauthenticated request redirects to the login page.
        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Api_requires_a_token()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/api/content/pages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

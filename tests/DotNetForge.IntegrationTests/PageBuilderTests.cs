using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNetForge.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The page editor (.docs/features/page-builder.md, .docs/pages/content-manager.md#page-editor): the builder's draft /
/// publish API and the rendered public page, page permissions and SEO, and the Profile screen.
/// </summary>
public sealed class PageBuilderTests
{
    /// <summary>A minimal layout: one section with a heading. The server fills every default.</summary>
    private const string Layout = """
        { "schema": 1, "root": "r", "settings": {},
          "nodes": {
            "r": { "id": "r", "type": "page", "parent": null, "children": ["s"], "props": {} },
            "s": { "id": "s", "type": "section", "parent": "r", "children": ["h"], "props": {} },
            "h": { "id": "h", "type": "heading", "parent": "s", "props": { "text": [{ "text": "Hello from the builder" }], "level": "1" } }
          } }
        """;

    private static async Task<Guid> PageIdAsync(DotNetForgeWebFactory factory, string slug)
    {
        Guid id = default;
        await factory.WithDbAsync(async db => id = await db.Pages.Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync());
        return id;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/admin/pages");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static string SaveBody(string? baseVersion, string document = Layout, bool force = false) =>
        $$"""{ "document": {{document}}, "baseVersion": {{(baseVersion is null ? "null" : $"\"{baseVersion}\"")}}, "force": {{(force ? "true" : "false")}} }""";

    [Fact]
    public async Task A_layout_is_drafted_published_and_rendered_on_the_site()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var id = await PageIdAsync(factory, "about");
        var api = $"/admin/pages/{id}/builder/";

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(api)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(api + "canvas")).StatusCode);

        // Save a draft: normalized and stored; visitors still see the default view.
        var saved = await PostJsonAsync(admin, api + "save", SaveBody(null));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var version = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetString();
        var state = await admin.GetFromJsonAsync<JsonElement>(api + "state");
        Assert.Equal(version, state.GetProperty("draft").GetProperty("version").GetString());
        Assert.Equal("1", state.GetProperty("draft").GetProperty("document").GetProperty("nodes").GetProperty("h").GetProperty("props").GetProperty("level").GetString());
        using var visitor = factory.CreateNoRedirectClient();
        Assert.DoesNotContain("Hello from the builder", await visitor.GetStringAsync("/about"));
        Assert.Contains("Hello from the builder", await admin.GetStringAsync(api + "preview"));

        // A save based on an older version is a conflict, unless forced.
        var stale = await PostJsonAsync(admin, api + "save", SaveBody(Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "save", SaveBody(Guid.NewGuid().ToString(), force: true))).StatusCode);

        // An invalid document is refused with a message.
        var invalid = await PostJsonAsync(admin, api + "save", SaveBody(null, """{ "schema": 2, "root": "r", "nodes": {} }""", force: true));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // Publish: the public page renders the layout, with its CSS as an external, cacheable stylesheet.
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "publish", "{}")).StatusCode);
        var html = await visitor.GetStringAsync("/about");
        Assert.Matches("<h1 class=\"[^\"]*pe-n-h[^\"]*\">Hello from the builder</h1>", html);
        var css = System.Text.RegularExpressions.Regex.Match(html, "/page-css/[0-9a-f-]+\\.css\\?v=[0-9a-f]+").Value;
        Assert.NotEmpty(css);
        var cssResponse = await visitor.GetAsync(css);
        Assert.Equal("text/css", cssResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", cssResponse.Headers.CacheControl?.ToString());
        var cssText = await cssResponse.Content.ReadAsStringAsync();
        Assert.True(cssText.Contains(".pe-n-"), cssText);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);

        // Unpublish: the default view again; the draft is kept.
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "unpublish", "{}")).StatusCode);
        Assert.DoesNotContain("Hello from the builder", await visitor.GetStringAsync("/about"));
        await factory.WithDbAsync(async db =>
        {
            var page = await db.Pages.SingleAsync(p => p.Id == id);
            Assert.Null(page.LayoutJson);
            Assert.NotNull(page.DraftLayoutJson);
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityId == id.ToString() && a.Details != null && a.Details.StartsWith("Layout")));
        });
    }

    [Fact]
    public async Task Only_users_who_may_edit_the_page_open_the_builder_and_publishing_needs_publish()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await PageIdAsync(factory, "about");
        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");

        // A seeded page has no creator: an Author (update.own only) may not edit it.
        Assert.Contains("/account/denied", (await author.GetAsync($"/admin/pages/{id}/builder")).Headers.Location?.OriginalString);

        // An edit grant on the page for the Author role opens it; publishing still needs the publish grant.
        Guid authorRole = default;
        await factory.WithDbAsync(async db =>
        {
            authorRole = await db.Roles.Where(r => r.Name == Roles.Author).Select(r => r.Id).SingleAsync();
            db.PagePermissions.Add(new Shared.Entities.PagePermission { PageId = id, RoleId = authorRole, Permission = "edit" });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/admin/pages/{id}/builder")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(author, $"/admin/pages/{id}/builder/save", SaveBody(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostJsonAsync(author, $"/admin/pages/{id}/builder/publish", "{}")).StatusCode);
    }

    [Fact]
    public async Task Page_permissions_seo_and_theme_are_saved_from_the_page_editor()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var id = await PageIdAsync(factory, "about");
        Guid authenticated = default;
        await factory.WithDbAsync(async db => authenticated = await db.Roles.Where(r => r.Name == Roles.Authenticated).Select(r => r.Id).SingleAsync());

        var fields = new List<KeyValuePair<string, string>>
        {
            new("Title", "About"), new("Slug", "about"), new("PageType", "Standard"), new("Published", "true"), new("DisplayInMenu", "true"),
            new("SortOrder", "1"), new("NoIndex", "true"), new("SocialImageUrl", "/media/x/share.png"),
            new("viewRoles", authenticated.ToString()), new("permissionsShown", "true"), new("tab", "permissions"),
        };
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, $"/admin/pages?selected={id}");
        var saved = await admin.PostAsync($"/admin/pages/update/{id}", new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.EndsWith("#permissions", saved.Headers.Location?.OriginalString);

        // Restricted to signed-in users: visitors are sent to sign in; signed-in users see it, with the SEO tags.
        using var visitor = factory.CreateNoRedirectClient();
        var anonymous = await visitor.GetAsync("/about");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/account/login", anonymous.Headers.Location?.OriginalString);
        var page = await admin.GetStringAsync("/about");
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", page);
        Assert.Contains("og:image", page);
        await factory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.PermissionChanged && a.EntityId == id.ToString())));

        // A theme that is not installed is refused.
        fields.RemoveAll(f => f.Key == "permissionsShown");
        fields.Add(new("ThemeId", "no-such-theme"));
        token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, $"/admin/pages?selected={id}");
        var refused = await admin.PostAsync($"/admin/pages/update/{id}", new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("That theme is not installed.", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Single_users_can_be_allowed_or_blocked_on_a_page()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var id = await PageIdAsync(factory, "about");
        var blocked = await factory.CreateUserAsync("blocked@example.com", "Edit0rPass1", Roles.Editor);
        var allowed = await factory.CreateUserAsync("allowed@example.com", "Auth0rPass1", Roles.Author);
        Guid superAdmin = default, adminId = default;
        await factory.WithDbAsync(async db => adminId = await db.Users.Where(u => u.Email == DotNetForgeWebFactory.AdminEmail).Select(u => u.Id).SingleAsync());
        superAdmin = adminId;

        async Task<HttpResponseMessage> SaveAsync(params (Guid User, string View, string Edit)[] rules)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new("Title", "About"), new("Slug", "about"), new("PageType", "Standard"), new("Published", "true"), new("DisplayInMenu", "true"),
                new("SortOrder", "1"), new("permissionsShown", "true"),
            };
            for (var i = 0; i < rules.Length; i++)
            {
                fields.Add(new($"userPermissions[{i}].UserId", rules[i].User.ToString()));
                fields.Add(new($"userPermissions[{i}].View", rules[i].View));
                fields.Add(new($"userPermissions[{i}].Edit", rules[i].Edit));
            }

            var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, $"/admin/pages?selected={id}");
            return await admin.PostAsync($"/admin/pages/update/{id}", new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))));
        }

        // Nobody blocks a Super Admin, or themselves.
        Assert.Contains("You can't block your own access to a page.", WebUtility.HtmlDecode(await (await SaveAsync((superAdmin, "block", ""))).Content.ReadAsStringAsync()));

        // Block an Editor from viewing and editing; allow an Author (who could only edit their own pages) to edit it.
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync((blocked, "block", "block"), (allowed, "", "allow"))).StatusCode);
        var editor = await factory.SignInAsync("blocked@example.com", "Edit0rPass1");
        var author = await factory.SignInAsync("allowed@example.com", "Auth0rPass1");
        Assert.Equal(HttpStatusCode.NotFound, (await editor.GetAsync("/about")).StatusCode);
        Assert.Contains("/account/denied", (await editor.GetAsync($"/admin/pages/{id}/builder")).Headers.Location?.OriginalString);
        var delete = await DotNetForgeWebFactory.PostFormAsync(editor, "/admin/pages", $"/admin/pages/delete/{id}", new Dictionary<string, string>());
        Assert.Contains("/account/denied", delete.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/admin/pages/{id}/builder")).StatusCode);

        // A block only on this page: others still see it, and it stays in the menu (no one is allowed explicitly).
        using var visitor = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/about")).StatusCode);

        // Clearing both rules removes the user; deleting a user removes their rules.
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync((blocked, "", ""), (allowed, "", "allow"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/about")).StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal(1, await db.PageUserPermissions.CountAsync(p => p.PageId == id)));
    }

    [Fact]
    public async Task Users_edit_their_own_profile_and_password()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var details = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/profile", "/admin/profile",
            new Dictionary<string, string> { ["FirstName"] = "Ada", ["LastName"] = "Admin", ["Email"] = DotNetForgeWebFactory.AdminEmail });
        Assert.Equal(HttpStatusCode.Redirect, details.StatusCode);
        Assert.Contains("Ada Admin", await admin.GetStringAsync("/admin/profile"));

        var wrong = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/profile", "/admin/profile/password",
            new Dictionary<string, string> { ["CurrentPassword"] = "nope", ["NewPassword"] = "N3wPassword", ["ConfirmPassword"] = "N3wPassword" });
        Assert.Contains("Your current password is not correct.", await wrong.Content.ReadAsStringAsync());

        var changed = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/profile", "/admin/profile/password",
            new Dictionary<string, string> { ["CurrentPassword"] = DotNetForgeWebFactory.AdminPassword, ["NewPassword"] = "N3wPassword", ["ConfirmPassword"] = "N3wPassword" });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        await factory.SignInAsync(DotNetForgeWebFactory.AdminEmail, "N3wPassword");
    }
}

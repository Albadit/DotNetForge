using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNetForge.Abstractions.Security;
using DotNetForge.Core.Content;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Structured content end to end (.docs/features/structured-content.md): content types from templates, the generated
/// entry form (drafts, publishing, validation, uniqueness, conflicts), the entry list, content lists on published pages
/// (bindings, filters, sorting, item links, detail pages), the page builder's content endpoints, the headless API and the
/// permissions around all of it. The whole workflow: type → fields → entries → page builder → publish.
/// </summary>
public sealed class StructuredContentTests
{
    private static async Task<HttpResponseMessage> CreateTypeAsync(HttpClient client, string template, string? name = null) =>
        await DotNetForgeWebFactory.PostFormAsync(client, "/admin/content-types/new", "/admin/content-types/create",
            new Dictionary<string, string> { ["template"] = template, ["name"] = name ?? string.Empty });

    private static async Task<HttpResponseMessage> SaveEntryAsync(HttpClient client, string type, IDictionary<string, string> fields,
        bool publish = true, Guid? id = null)
    {
        var url = id is null ? $"/admin/content/{type}/new" : $"/admin/content/{type}/{id}";
        var form = new Dictionary<string, string>(fields) { ["intent"] = publish ? "publish" : "save" };
        return await DotNetForgeWebFactory.PostFormAsync(client, url, url, form);
    }

    private static Guid IdFrom(HttpResponseMessage response) =>
        Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());

    private static async Task<ContentType> TypeAsync(DotNetForgeWebFactory factory, string slug)
    {
        ContentType type = null!;
        await factory.WithDbAsync(async db => type = await db.ContentTypes.AsNoTracking().SingleAsync(t => t.Slug == slug));
        return type;
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string tokenPage, string url, string json)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, tokenPage);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    /// <summary>A layout with one content list of products: name, price line and a link per entry.</summary>
    private static string ProductList(Guid typeId, string extraProps = "") => $$"""
        { "schema": 1, "root": "r", "settings": {},
          "nodes": {
            "r": { "id": "r", "type": "page", "parent": null, "children": ["s"], "props": {} },
            "s": { "id": "s", "type": "section", "parent": "r", "children": ["c"], "props": {}, "layout": { "base": { "mode": "stack" } } },
            "c": { "id": "c", "type": "collection", "parent": "s", "children": ["i"],
                   "props": { "source": "{{typeId}}", "itemUrl": "/news/{slug}", "sortField": "price", "sortDir": "desc"{{extraProps}} } },
            "i": { "id": "i", "type": "item", "parent": "c", "children": ["h", "p", "b"], "props": {} },
            "h": { "id": "h", "type": "heading", "parent": "i", "props": {}, "bind": { "text": "{name}" } },
            "p": { "id": "p", "type": "text", "parent": "i", "props": {}, "bind": { "content": "From {price} USD" } },
            "b": { "id": "b", "type": "button", "parent": "i", "props": { "label": "View" }, "bind": { "href": "{$url}" } }
          } }
        """;

    private static async Task<string> ApiTokenAsync(DotNetForgeWebFactory factory, params string[] permissions)
    {
        var plaintext = string.Empty;
        await factory.WithDbAsync(async db =>
        {
            var generated = factory.Services.GetRequiredService<IApiTokenFactory>().Generate();
            plaintext = generated.Plaintext;
            db.ApiTokens.Add(new ApiToken
            {
                Name = "content " + Guid.NewGuid().ToString("N")[..6],
                PermissionsCsv = string.Join(',', permissions),
                TokenHash = generated.Hash,
                TokenPrefix = generated.Prefix,
                ExpirationDate = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        });
        return plaintext;
    }

    [Fact]
    public async Task A_template_creates_the_type_and_the_types_it_links_to_and_renamed_fields_keep_their_values()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var created = await CreateTypeAsync(admin, "blog-posts");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var slugs = await db.ContentTypes.Select(t => t.Slug).ToListAsync();
            Assert.Equal(new[] { "authors", "blog-posts", "categories" }, slugs.Order());
        });

        var blog = await TypeAsync(factory, "blog-posts");
        var categories = await TypeAsync(factory, "categories");
        var fields = ContentSchema.Parse(blog.FieldsJson);
        Assert.Equal(categories.Id, fields.Single(f => f.Key == "category").RelationTo);

        var category = IdFrom(await SaveEntryAsync(admin, "categories", new Dictionary<string, string> { ["f.name"] = "Design" }));
        var post = IdFrom(await SaveEntryAsync(admin, "blog-posts", new Dictionary<string, string>
        {
            ["f.title"] = "Hello world",
            ["f.excerpt"] = "First post",
            ["f.category"] = category.ToString(),
        }));

        // The slug is made from the title when left empty (as the form script does while typing).
        await factory.WithDbAsync(async db =>
            Assert.Contains("\"slug\":\"hello-world\"", (await db.ContentEntries.SingleAsync(e => e.Id == post)).DataJson));

        // Rename "excerpt" to "summary": the field keeps its id, so the value moves with it.
        var renamed = blog.FieldsJson.Replace("\"key\":\"excerpt\"", "\"key\":\"summary\"");
        var save = await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content-types/{blog.Id}", $"/admin/content-types/{blog.Id}",
            new Dictionary<string, string>
            {
                ["Name"] = blog.Name,
                ["Slug"] = blog.Slug,
                ["Icon"] = blog.Icon,
                ["Kind"] = "Collection",
                ["Drafts"] = "true",
                ["TitleField"] = "title",
                ["FieldsJson"] = renamed,
            });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var entry = await db.ContentEntries.SingleAsync(e => e.Id == post);
            Assert.Contains("\"summary\":\"First post\"", entry.DataJson);
            Assert.Contains("\"summary\":\"First post\"", entry.PublishedDataJson);
            Assert.DoesNotContain("excerpt", entry.DataJson);
            Assert.True(await db.ContentEntryValues.AnyAsync(v => v.EntryId == post && v.Field == "summary"));
        });

        // A type another type links to can't be deleted.
        var refused = await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content-types/{categories.Id}",
            $"/admin/content-types/{categories.Id}/delete", new Dictionary<string, string> { ["withEntries"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        await factory.WithDbAsync(async db => Assert.True(await db.ContentTypes.AnyAsync(t => t.Id == categories.Id)));
    }

    [Fact]
    public async Task Entries_are_drafted_validated_published_listed_and_protected_from_stale_saves()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await CreateTypeAsync(admin, "products");

        // Publishing needs the required fields; a draft does not.
        var refused = await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Chair" });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Price is required.", await refused.Content.ReadAsStringAsync());
        var draft = await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Chair", ["f.sku"] = "CH-1" }, publish: false);
        Assert.Equal(HttpStatusCode.Redirect, draft.StatusCode);
        var chair = IdFrom(draft);

        // Values are checked against the field: a negative price is refused, a unique SKU can't repeat.
        var negative = await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Lamp", ["f.price"] = "-3" });
        Assert.Contains("Price: must be at least 0.", await negative.Content.ReadAsStringAsync());
        var duplicate = await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Lamp", ["f.price"] = "3", ["f.sku"] = "ch-1" });
        Assert.Contains("must be unique", await duplicate.Content.ReadAsStringAsync());

        var lamp = IdFrom(await SaveEntryAsync(admin, "products", new Dictionary<string, string>
        {
            ["f.name"] = "Lamp",
            ["f.price"] = "49.5",
            ["f.sku"] = "LA-1",
            ["f.inStock"] = "true",
            ["f.features"] = "LED\nDimmable",
            ["f.variants.0.name"] = "White",
            ["f.variants.0.price"] = "49.5",
            ["f.variants.7.name"] = "Black",
        }));
        await factory.WithDbAsync(async db =>
        {
            var entry = await db.ContentEntries.SingleAsync(e => e.Id == lamp);
            Assert.True(entry.Published);
            Assert.False(entry.HasDraft);
            Assert.Contains("\"features\":[\"LED\",\"Dimmable\"]", entry.DataJson);
            Assert.Contains("\"variants\":[{\"name\":\"White\",\"price\":49.5},{\"name\":\"Black\"}]", entry.DataJson);
            var draftEntry = await db.ContentEntries.SingleAsync(e => e.Id == chair);
            Assert.False(draftEntry.Published);
        });

        // The list: status filter, search and sorting run on the server.
        var list = await admin.GetStringAsync("/admin/content/products?status=draft");
        Assert.Contains("Chair", list);
        Assert.DoesNotContain(">Lamp<", list);
        Assert.Contains(">Lamp<", await admin.GetStringAsync("/admin/content/products?q=lam"));
        var byPrice = await admin.GetStringAsync("/admin/content/products?sort=price&desc=true");
        Assert.True(byPrice.IndexOf(">Lamp<", StringComparison.Ordinal) < byPrice.IndexOf(">Chair<", StringComparison.Ordinal));

        // A save based on an older version is refused, and offered again as an overwrite.
        var stale = await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content/products/{lamp}", $"/admin/content/products/{lamp}",
            new Dictionary<string, string> { ["f.name"] = "Lamp", ["f.price"] = "5", ["baseVersion"] = Guid.NewGuid().ToString(), ["intent"] = "save" });
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Someone else saved this entry", await stale.Content.ReadAsStringAsync());

        // Duplicate makes an unpublished copy whose unique values are made unique again.
        var copy = await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content/products/{lamp}", $"/admin/content/products/{lamp}/duplicate", new Dictionary<string, string>());
        var copyId = IdFrom(copy);
        await factory.WithDbAsync(async db =>
        {
            var entry = await db.ContentEntries.SingleAsync(e => e.Id == copyId);
            Assert.False(entry.Published);
            Assert.Equal("Lamp (copy)", entry.Title);
            Assert.Contains("\"slug\":\"lamp-copy\"", entry.DataJson);
            Assert.DoesNotContain("LA-1", entry.DataJson);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.EntryPublished));
        });

        // Bulk delete removes the entries and their index rows.
        var deleted = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/content/products", "/admin/content/products/delete-selected",
            new Dictionary<string, string> { ["ids"] = copyId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.ContentEntries.AnyAsync(e => e.Id == copyId));
            Assert.False(await db.ContentEntryValues.AnyAsync(v => v.EntryId == copyId));
        });
    }

    [Fact]
    public async Task Pages_show_content_lists_and_detail_pages_and_follow_entry_edits()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await CreateTypeAsync(admin, "products");
        var type = await TypeAsync(factory, "products");
        var chair = IdFrom(await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Oak Chair", ["f.price"] = "1249.5", ["f.inStock"] = "true" }));
        await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Desk", ["f.price"] = "300", ["f.inStock"] = "true" });
        await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Sold out", ["f.price"] = "10", ["f.inStock"] = "false" });
        await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Unreleased", ["f.price"] = "20" }, publish: false);

        Guid newsId = default;
        await factory.WithDbAsync(async db =>
        {
            var news = await db.Pages.SingleAsync(p => p.Slug == "news");
            newsId = news.Id;
            news.LayoutJson = ProductList(type.Id, ", \"filters\": [{ \"field\": \"inStock\", \"op\": \"eq\", \"value\": \"true\" }]");
            // A detail page: /news/[slug] shows the product its URL names.
            db.Pages.Add(new Page
            {
                ParentPageId = news.Id,
                Slug = "[slug]",
                Title = "Product",
                Published = true,
                LayoutJson = ProductList(type.Id, ", \"mode\": \"single\", \"routeParam\": \"slug\", \"routeField\": \"slug\""),
            });
            await db.SaveChangesAsync();
        });

        using var visitor = factory.CreateNoRedirectClient();
        var html = await visitor.GetStringAsync("/news");
        // Published, in stock, most expensive first; values formatted by the field (2 decimals).
        Assert.Matches("Oak Chair</h2>.*From 1,249.50 USD.*href=\"/news/oak-chair\".*Desk</h2>.*From 300.00 USD", html.Replace("\n", " "));
        Assert.DoesNotContain("Sold out", html);
        Assert.DoesNotContain("Unreleased", html);

        // The detail page: the entry its URL names, titled after it; an unknown one is a 404.
        var detail = await visitor.GetStringAsync("/news/oak-chair");
        Assert.Contains("<title>Oak Chair", detail);
        Assert.DoesNotContain("Desk</h2>", detail);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/news/no-such-product")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/news/unreleased")).StatusCode);

        // Editing the entry changes every page that shows it, without touching the pages.
        await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Walnut Chair", ["f.slug"] = "oak-chair", ["f.price"] = "1249.5", ["f.inStock"] = "true" }, id: chair);
        Assert.Contains("Walnut Chair", await visitor.GetStringAsync("/news"));
        Assert.Contains("Walnut Chair", await visitor.GetStringAsync("/news/oak-chair"));

        // Pagination: one per page, the second page from ?pg-{node}=2.
        await factory.WithDbAsync(async db =>
        {
            var news = await db.Pages.SingleAsync(p => p.Id == newsId);
            news.LayoutJson = ProductList(type.Id, ", \"limit\": 1, \"paginate\": true");
            await db.SaveChangesAsync();
        });
        var second = await visitor.GetStringAsync("/news?pg-c=2");
        Assert.Contains("<span class=\"pe-pager-status\">2 / 3</span>", second);
        Assert.Contains("Desk</h2>", second);
    }

    [Fact]
    public async Task The_page_builder_lists_content_types_and_previews_entries()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await CreateTypeAsync(admin, "products");
        var type = await TypeAsync(factory, "products");
        await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Lamp", ["f.price"] = "12" });
        Guid pageId = default;
        await factory.WithDbAsync(async db => pageId = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync());
        var api = $"/admin/pages/{pageId}/builder/";

        var types = await admin.GetFromJsonAsync<JsonElement>(api + "content/types");
        var products = types.EnumerateArray().Single(t => t.GetProperty("slug").GetString() == "products");
        var paths = products.GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("path").GetString()).ToList();
        Assert.Contains("$title", paths);
        Assert.Contains("$url", paths);
        Assert.Contains("price", paths);

        var preview = await PostJsonAsync(admin, api, api + "content/preview",
            $$"""{ "lists": [{ "id": "c", "props": { "source": "{{type.Id}}", "itemUrl": "/shop/{slug}" } }, { "id": "BAD id", "props": {} }] }""");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var data = await preview.Content.ReadFromJsonAsync<JsonElement>();
        var item = data.GetProperty("c").GetProperty("items")[0].GetProperty("fields");
        Assert.Equal("Lamp", item.GetProperty("name").GetProperty("text").GetString());
        Assert.Equal("12.00", item.GetProperty("price").GetProperty("text").GetString());
        Assert.Equal("/shop/lamp", item.GetProperty("$url").GetProperty("url").GetString());
        Assert.False(data.TryGetProperty("BAD id", out _));

        // A layout with a content list is saved, published and rendered like any other.
        var save = await PostJsonAsync(admin, api, api + "save", $$"""{ "document": {{ProductList(type.Id)}}, "baseVersion": null, "force": true }""");
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api, api + "publish", "{}")).StatusCode);
        Assert.Contains("Lamp</h2>", await factory.CreateNoRedirectClient().GetStringAsync("/about"));
    }

    [Fact]
    public async Task The_api_reads_and_writes_entries_within_its_permissions()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await CreateTypeAsync(admin, "blog-posts");
        var category = IdFrom(await SaveEntryAsync(admin, "categories", new Dictionary<string, string> { ["f.name"] = "Design" }));
        await SaveEntryAsync(admin, "blog-posts", new Dictionary<string, string> { ["f.title"] = "Draft only" }, publish: false);

        using var reader = factory.CreateNoRedirectClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await ApiTokenAsync(factory, PermissionKeys.EntriesRead, PermissionKeys.SchemaRead));
        using var writer = factory.CreateNoRedirectClient();
        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await ApiTokenAsync(factory, PermissionKeys.EntriesRead, PermissionKeys.EntriesCreate, PermissionKeys.EntriesUpdate, PermissionKeys.EntriesPublish));

        var schema = await reader.GetFromJsonAsync<JsonElement>("/api/content-types/blog-posts");
        Assert.Equal("relationship", schema.GetProperty("data").GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "category").GetProperty("type").GetString());

        // Reading needs entries.read; writing needs more.
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/entries/blog-posts", new { data = new { title = "x" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/entries/blog-posts?status=draft")).StatusCode);

        // Create (validated like the form), published at once.
        var invalid = await writer.PostAsJsonAsync("/api/entries/blog-posts", new { data = new { excerpt = "no title" }, publish = true });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Title is required.", await invalid.Content.ReadAsStringAsync());
        var created = await writer.PostAsJsonAsync("/api/entries/blog-posts", new { data = new { title = "From the API", category = category }, publish = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entry = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("published", entry.GetProperty("status").GetString());

        // Reads: published only, relationships expanded, filters and slugs.
        var list = await reader.GetFromJsonAsync<JsonElement>($"/api/entries/blog-posts?filter[category]={category}");
        Assert.Equal(1, list.GetProperty("meta").GetProperty("total").GetInt32());
        var first = list.GetProperty("data")[0];
        Assert.Equal("Design", first.GetProperty("data").GetProperty("category").GetProperty("title").GetString());
        var bySlug = await reader.GetFromJsonAsync<JsonElement>("/api/entries/blog-posts/from-the-api?depth=0");
        Assert.Equal(category.ToString(), bySlug.GetProperty("data").GetProperty("data").GetProperty("category").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/api/entries/blog-posts/draft-only")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.GetAsync("/api/entries/blog-posts?filter[title][like]=x")).StatusCode);

        // Update with the version read; an older version is a conflict.
        var id = entry.GetProperty("id").GetString();
        var version = entry.GetProperty("version").GetString();
        var updated = await writer.PutAsJsonAsync($"/api/entries/blog-posts/{id}", new { data = new { title = "Renamed" }, version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("changed", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await writer.PutAsJsonAsync($"/api/entries/blog-posts/{id}", new { data = new { title = "Again" }, version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.DeleteAsync($"/api/entries/blog-posts/{id}")).StatusCode);
    }

    [Fact]
    public async Task Roles_decide_who_defines_types_and_who_edits_and_publishes_entries()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await CreateTypeAsync(admin, "faq");
        var others = IdFrom(await SaveEntryAsync(admin, "faq", new Dictionary<string, string> { ["f.question"] = "Why?", ["f.answer"] = "Because." }));
        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");

        // Only admins define content types (the grants were added to existing roles by the seeder).
        Assert.Contains("/account/denied", (await editor.GetAsync("/admin/content-types")).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/admin/content/faq")).StatusCode);

        // Authors write drafts, can't publish, and edit only their own entries.
        var published = await SaveEntryAsync(author, "faq", new Dictionary<string, string> { ["f.question"] = "How?", ["f.answer"] = "Like this." });
        Assert.Contains("permission to publish", await published.Content.ReadAsStringAsync());
        var own = await SaveEntryAsync(author, "faq", new Dictionary<string, string> { ["f.question"] = "How?" }, publish: false);
        Assert.Equal(HttpStatusCode.Redirect, own.StatusCode);
        var foreign = await SaveEntryAsync(author, "faq", new Dictionary<string, string> { ["f.question"] = "Hacked" }, publish: false, id: others);
        Assert.Contains("/account/denied", foreign.Headers.Location?.OriginalString);

        // Editors publish anyone's entries.
        Assert.Equal(HttpStatusCode.Redirect, (await SaveEntryAsync(editor, "faq", new Dictionary<string, string> { ["f.question"] = "Why not?", ["f.answer"] = "Fine." }, id: others)).StatusCode);
    }

    [Fact]
    public async Task The_content_screens_render_cleanly_and_old_content_manager_links_lead_to_pages()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await CreateTypeAsync(admin, "products");
        await CreateTypeAsync(admin, "site-settings");
        var type = await TypeAsync(factory, "products");
        var entry = IdFrom(await SaveEntryAsync(admin, "products", new Dictionary<string, string> { ["f.name"] = "Lamp", ["f.price"] = "3" }));

        foreach (var path in new[] { "/admin/content", $"/admin/content-types/{type.Id}", "/admin/content/products", "/admin/content/products/new", $"/admin/content/products/{entry}" })
        {
            var response = await admin.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}.");
            AdminUiTests.AssertWellFormed(path, await response.Content.ReadAsStringAsync());
        }

        // API token scopes are grouped under the screens' names: pages, the Content Manager, content types.
        var tokenForm = await admin.GetStringAsync("/admin/api-tokens/create");
        Assert.Contains("<span>Pages</span>", tokenForm);
        Assert.Contains("<span>Content Manager</span>", tokenForm);
        Assert.Contains("<span>Content types</span>", tokenForm);

        // A single type opens its one entry.
        var single = await admin.GetAsync("/admin/content/site-settings");
        Assert.StartsWith("/admin/content/site-settings/", single.Headers.Location?.OriginalString);

        // The page tree moved to /admin/pages; its forms post there, and old links still arrive.
        Guid pageId = default;
        await factory.WithDbAsync(async db => pageId = await db.Pages.Select(p => p.Id).FirstAsync());
        var pages = await admin.GetStringAsync($"/admin/pages?selected={pageId}");
        Assert.Contains($"action=\"/admin/pages/update/{pageId}\"", pages);
        Assert.Contains($"action=\"/admin/pages/delete/{pageId}\"", pages);
        Assert.Equal($"/admin/pages?selected={pageId}", (await admin.GetAsync($"/admin/content?selected={pageId}")).Headers.Location?.OriginalString);
        Assert.Equal($"/admin/pages/{pageId}/builder", (await admin.GetAsync($"/admin/content/{pageId}/builder")).Headers.Location?.OriginalString);
    }
}

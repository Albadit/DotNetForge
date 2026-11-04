using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotNetForge.Abstractions.Security;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>Translated entries: the Content Manager, the API, content lists and the default switch (.docs/features/internationalization.md#entries).</summary>
public sealed class EntryTranslationTests
{
    /// <summary>A site with German and a localized Products type holding one published chair; signed in as the admin.</summary>
    private static async Task<(DotNetForgeWebFactory Factory, HttpClient Admin, Guid Chair, Guid German)> SiteAsync(bool localized = true)
    {
        var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", "/admin/internationalization/add",
            new Dictionary<string, string> { ["Code"] = "de", ["DisplayName"] = "Deutsch", ["Enabled"] = "true" });
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/content-types/new", "/admin/content-types/create",
            new Dictionary<string, string> { ["template"] = "products", ["name"] = string.Empty });
        await factory.WithDbAsync(async db =>
        {
            (await db.ContentTypes.SingleAsync(t => t.Slug == "products")).Localized = localized;
            await db.SaveChangesAsync();
        });
        var created = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/content/products/new", "/admin/content/products/new",
            new Dictionary<string, string> { ["f.name"] = "Chair", ["f.price"] = "10", ["f.sku"] = "CH-1", ["intent"] = "publish" });
        var chair = Guid.Parse(created.Headers.Location!.OriginalString.Split('/').Last());
        Guid german = default;
        await factory.WithDbAsync(async db => german = await db.Locales.Where(l => l.Code == "de").Select(l => l.Id).SingleAsync());
        return (factory, admin, chair, german);
    }

    private static Task<HttpResponseMessage> TranslateAsync(HttpClient admin, Guid chair, string name, bool publish) =>
        DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content/products/{chair}/translations/de", $"/admin/content/products/{chair}/translations/de",
            new Dictionary<string, string> { ["f.name"] = name, ["intent"] = publish ? "publish" : "save" });

    private static async Task<HttpClient> ApiAsync(DotNetForgeWebFactory factory, params string[] permissions)
    {
        var plaintext = string.Empty;
        await factory.WithDbAsync(async db =>
        {
            var generated = factory.Services.GetRequiredService<IApiTokenFactory>().Generate();
            plaintext = generated.Plaintext;
            db.ApiTokens.Add(new ApiToken
            {
                Name = "i18n " + Guid.NewGuid().ToString("N")[..6],
                PermissionsCsv = string.Join(',', permissions),
                TokenHash = generated.Hash,
                TokenPrefix = generated.Prefix,
                ExpirationDate = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        });
        var client = factory.CreateNoRedirectClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        return client;
    }

    private static async Task<JsonElement> DataAsync(HttpClient api, string url)
    {
        var response = await api.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
    }

    [Fact]
    public async Task A_translation_is_saved_as_a_draft_then_published_and_read_with_the_shared_fields()
    {
        var (factory, admin, chair, _) = await SiteAsync();
        using var _ = factory;
        var form = await admin.GetStringAsync($"/admin/content/products/{chair}/translations/de");
        Assert.Contains("Translating into Deutsch", form);
        Assert.Contains("name=\"f.name\"", form);
        Assert.DoesNotContain("name=\"f.price\"", form); // shared, not translated
        Assert.DoesNotContain("name=\"f.sku\"", form); // unique: the same in every language

        Assert.Equal(HttpStatusCode.Redirect, (await TranslateAsync(admin, chair, "Stuhl", publish: false)).StatusCode);
        var api = await ApiAsync(factory, PermissionKeys.EntriesRead, PermissionKeys.EntriesUpdate);
        Assert.Equal("Chair", (await DataAsync(api, $"/api/entries/products/{chair}?locale=de")).GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("Stuhl", (await DataAsync(api, $"/api/entries/products/{chair}?locale=de&status=draft")).GetProperty("data").GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.Redirect, (await TranslateAsync(admin, chair, "Stuhl", publish: true)).StatusCode);
        var german = await DataAsync(api, $"/api/entries/products/{chair}?locale=de");
        Assert.Equal(("Stuhl", "Stuhl", "de"), (german.GetProperty("data").GetProperty("name").GetString(), german.GetProperty("title").GetString(), german.GetProperty("locale").GetString()));
        Assert.Equal(10, german.GetProperty("data").GetProperty("price").GetDecimal());
        Assert.Equal("Chair", (await DataAsync(api, $"/api/entries/products/{chair}")).GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync($"/api/entries/products/{chair}?locale=xx")).StatusCode);
        await factory.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.EntryTranslated && a.EntityId == chair.ToString())));
    }

    [Fact]
    public async Task The_api_writes_translations_of_localized_types_only()
    {
        var (factory, _, chair, _) = await SiteAsync();
        using var __ = factory;
        var api = await ApiAsync(factory, PermissionKeys.EntriesRead, PermissionKeys.EntriesUpdate, PermissionKeys.EntriesPublish);

        var put = await api.PutAsync($"/api/entries/products/{chair}?locale=de",
            new StringContent("""{ "data": { "name": "Sedia", "price": 999 }, "publish": true }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var german = await DataAsync(api, $"/api/entries/products/{chair}?locale=de");
        Assert.Equal("Sedia", german.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal(10, german.GetProperty("data").GetProperty("price").GetDecimal()); // a shared field is not changed by a translation
    }

    [Fact]
    public async Task A_type_that_is_not_localized_has_no_translations()
    {
        var (factory, admin, chair, _) = await SiteAsync(localized: false);
        using var _ = factory;
        var api = await ApiAsync(factory, PermissionKeys.EntriesRead, PermissionKeys.EntriesUpdate);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/content/products/{chair}/translations/de")).StatusCode);
        var put = await api.PutAsync($"/api/entries/products/{chair}?locale=de",
            new StringContent("""{ "data": { "name": "Stuhl" } }""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Making_a_locale_the_default_moves_entry_values_and_rebuilds_the_title()
    {
        var (factory, admin, chair, german) = await SiteAsync();
        using var _ = factory;
        await TranslateAsync(admin, chair, "Stuhl", publish: true);

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", $"/admin/internationalization/{german}/default", new Dictionary<string, string>());

        await factory.WithDbAsync(async db =>
        {
            var entry = await db.ContentEntries.SingleAsync(e => e.Id == chair);
            Assert.Equal("Stuhl", entry.Title);
            Assert.Contains("\"name\":\"Stuhl\"", entry.PublishedDataJson);
            var english = await db.Locales.SingleAsync(l => l.Code == "en");
            Assert.Contains("\"name\":\"Chair\"", (await db.ContentEntryTranslations.SingleAsync(t => t.EntryId == chair && t.LocaleId == english.Id)).PublishedDataJson);
            Assert.True(await db.ContentEntryValues.AnyAsync(v => v.EntryId == chair && v.Field == "name" && v.Text == "stuhl")); // the index follows
        });
        var api = await ApiAsync(factory, PermissionKeys.EntriesRead);
        Assert.Equal("Chair", (await DataAsync(api, $"/api/entries/products/{chair}?locale=en")).GetProperty("data").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_content_list_on_a_translated_page_shows_the_translation_and_links_in_the_locale()
    {
        var (factory, admin, chair, _) = await SiteAsync();
        using var _ = factory;
        await TranslateAsync(admin, chair, "Stuhl", publish: true);
        Guid about = default, products = default;
        await factory.WithDbAsync(async db =>
        {
            about = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync();
            products = await db.ContentTypes.Where(t => t.Slug == "products").Select(t => t.Id).SingleAsync();
        });
        var layout = $$"""
            { "schema": 1, "root": "r", "settings": {},
              "nodes": {
                "r": { "id": "r", "type": "page", "parent": null, "children": ["s"], "props": {} },
                "s": { "id": "s", "type": "section", "parent": "r", "children": ["c"], "props": {} },
                "c": { "id": "c", "type": "collection", "parent": "s", "children": ["i"], "props": { "source": "{{products}}", "itemUrl": "/shop/{slug}" } },
                "i": { "id": "i", "type": "item", "parent": "c", "children": ["h", "b"], "props": {} },
                "h": { "id": "h", "type": "heading", "parent": "i", "props": {}, "bind": { "text": "{name}" } },
                "b": { "id": "b", "type": "button", "parent": "i", "props": { "label": "View" }, "bind": { "href": "{$url}" } }
              } }
            """;
        var api = $"/admin/pages/{about}/builder/";
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "save", $$"""{ "document": {{layout}}, "baseVersion": null, "force": false }""")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "publish", "{}")).StatusCode);

        using var visitor = factory.CreateNoRedirectClient();
        var main = await visitor.GetStringAsync("/about");
        var de = await visitor.GetStringAsync("/de/about");

        Assert.Contains("Chair", main);
        Assert.Contains("href=\"/shop/chair\"", main);
        Assert.Contains("Stuhl", de); // the page has no German layout: the main one, with German values
        Assert.DoesNotContain(">Chair<", de);
        Assert.Contains("href=\"/de/shop/chair\"", de);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/admin/pages");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Deleting_or_duplicating_an_entry_handles_its_translations()
    {
        var (factory, admin, chair, german) = await SiteAsync();
        using var _ = factory;
        await TranslateAsync(admin, chair, "Stuhl", publish: true);

        var duplicate = await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content/products/{chair}", $"/admin/content/products/{chair}/duplicate", new Dictionary<string, string>());
        var copy = Guid.Parse(duplicate.Headers.Location!.OriginalString.Split('/').Last());
        await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/content/products/{chair}", $"/admin/content/products/{chair}/delete", new Dictionary<string, string>());

        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.ContentEntryTranslations.AnyAsync(t => t.EntryId == chair));
            var copied = await db.ContentEntryTranslations.SingleAsync(t => t.EntryId == copy && t.LocaleId == german);
            Assert.Contains("Stuhl", copied.DataJson);
            Assert.Null(copied.PublishedDataJson); // the copy is unpublished, like the entry
        });
    }
}

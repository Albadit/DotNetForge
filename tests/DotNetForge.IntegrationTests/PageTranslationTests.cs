using System.Net;
using System.Text;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>Translated content pages: texts, layouts, the public URLs and the default switch (.docs/features/internationalization.md#content-pages).</summary>
public sealed class PageTranslationTests
{
    private const string GermanLayout = """
        { "schema": 1, "root": "r", "settings": {},
          "nodes": {
            "r": { "id": "r", "type": "page", "parent": null, "children": ["s"], "props": {} },
            "s": { "id": "s", "type": "section", "parent": "r", "children": ["h"], "props": {} },
            "h": { "id": "h", "type": "heading", "parent": "s", "props": { "text": [{ "text": "Hallo aus dem Baukasten" }], "level": "1" } }
          } }
        """;

    /// <summary>A fresh site with German added, signed in as the admin; returns the about page and German's ids.</summary>
    private static async Task<(DotNetForgeWebFactory Factory, HttpClient Admin, Guid About, Guid German)> SiteAsync()
    {
        var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", "/admin/internationalization/add",
            new Dictionary<string, string> { ["Code"] = "de", ["DisplayName"] = "Deutsch", ["Enabled"] = "true" });
        Guid about = default, german = default;
        await factory.WithDbAsync(async db =>
        {
            about = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync();
            german = await db.Locales.Where(l => l.Code == "de").Select(l => l.Id).SingleAsync();
        });
        return (factory, admin, about, german);
    }

    /// <summary>Saves the about page form with a German title and description.</summary>
    private static async Task<HttpResponseMessage> TranslateAboutAsync(HttpClient admin, Guid about, Guid german, string title, string? description = null)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Title", "About"), new("Slug", "about"), new("PageType", "Standard"), new("Published", "true"), new("DisplayInMenu", "true"),
            new("SortOrder", "1"), new("MetaDescription", "Who we are"), new("tab", "translations"),
            new("translations[0].LocaleId", german.ToString()), new("translations[0].Title", title),
            new("translations[0].MetaDescription", description ?? string.Empty),
        };
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, $"/admin/pages?selected={about}");
        return await admin.PostAsync($"/admin/pages/update/{about}", new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))));
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, string json)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/admin/pages");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_translated_page_shows_its_texts_under_the_locale_prefix_and_falls_back_elsewhere()
    {
        var (factory, admin, about, german) = await SiteAsync();
        using var _ = factory;

        var saved = await TranslateAboutAsync(admin, about, german, "Ueber uns");
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.EndsWith("#translations", saved.Headers.Location?.OriginalString);

        using var visitor = factory.CreateNoRedirectClient();
        var de = await visitor.GetStringAsync("/de/about");
        var en = await visitor.GetStringAsync("/about");

        Assert.Contains("<title>Ueber uns", de);
        Assert.Contains("<html lang=\"de\"", de);
        Assert.Contains("content=\"Who we are\"", de); // no German description: the main one shows
        Assert.Contains("href=\"/de/about\"", de); // the menu link stays in German
        Assert.Contains("hreflang=\"de\"", de);
        Assert.Contains("hreflang=\"en\"", de);
        // The language select: the page in each language, the current one chosen.
        Assert.Contains("<option value=\"/de/about\" lang=\"de\" selected=\"selected\">", de);
        Assert.Contains("<option value=\"/about\" lang=\"en\">", de);
        Assert.Contains("<title>About", en);
        Assert.DoesNotContain("Ueber uns", en);
        await factory.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.ContentTranslated && a.EntityId == about.ToString())));
    }

    [Fact]
    public async Task A_layout_is_drafted_and_published_per_locale()
    {
        var (factory, admin, about, _) = await SiteAsync();
        using var __ = factory;
        var api = $"/admin/pages/{about}/translations/de/builder/";

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(api)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/pages/{about}/translations/fr/builder/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/pages/{about}/translations/en/builder/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "save", $$"""{ "document": {{GermanLayout}}, "baseVersion": null, "force": false }""")).StatusCode);

        using var visitor = factory.CreateNoRedirectClient();
        Assert.DoesNotContain("Hallo aus dem Baukasten", await visitor.GetStringAsync("/de/about")); // a draft only
        var preview = await admin.GetStringAsync(api + "preview");
        Assert.Contains("Hallo aus dem Baukasten", preview);
        Assert.Contains($"/admin/pages/{about}/translations/de/builder/draft.css?v=", preview); // the translation's draft stylesheet

        Assert.Equal(HttpStatusCode.OK, (await PostJsonAsync(admin, api + "publish", "{}")).StatusCode);

        var german = await visitor.GetStringAsync("/de/about");
        Assert.Contains("Hallo aus dem Baukasten", german);
        Assert.DoesNotContain("Hallo aus dem Baukasten", await visitor.GetStringAsync("/about"));

        // Its own stylesheet, though the page has no main layout.
        Assert.Contains($"/de/page-css/{about}.css?v=", german);
        var css = await visitor.GetAsync($"/de/page-css/{about}.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/page-css/{about}.css")).StatusCode);
        await factory.WithDbAsync(async db =>
            Assert.Null(await db.Pages.Where(p => p.Id == about).Select(p => p.LayoutJson).SingleAsync())); // the main layout is untouched
    }

    [Fact]
    public async Task Making_a_locale_the_default_moves_its_translations_into_the_pages()
    {
        var (factory, admin, about, german) = await SiteAsync();
        using var _ = factory;
        await TranslateAboutAsync(admin, about, german, "Ueber uns", "Wer wir sind");

        var result = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", $"/admin/internationalization/{german}/default",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);

        await factory.WithDbAsync(async db =>
        {
            var page = await db.Pages.SingleAsync(p => p.Id == about);
            Assert.Equal(("Ueber uns", "Wer wir sind"), (page.Title, page.MetaDescription));
            var english = await db.Locales.SingleAsync(l => l.Code == "en");
            var old = await db.PageTranslations.SingleAsync(t => t.PageId == about && t.LocaleId == english.Id);
            Assert.Equal(("About", "Who we are"), (old.Title, old.MetaDescription));
            Assert.False(await db.PageTranslations.AnyAsync(t => t.LocaleId == german));
        });
        using var visitor = factory.CreateNoRedirectClient();
        Assert.Contains("<title>Ueber uns", await visitor.GetStringAsync("/about"));
        Assert.Contains("<title>About", await visitor.GetStringAsync("/en/about"));
    }

    [Fact]
    public async Task Deleting_or_duplicating_a_page_handles_its_translations()
    {
        var (factory, admin, about, german) = await SiteAsync();
        using var _ = factory;
        await TranslateAboutAsync(admin, about, german, "Ueber uns");

        await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/pages?selected={about}", $"/admin/pages/duplicate/{about}", new Dictionary<string, string>());
        await DotNetForgeWebFactory.PostFormAsync(admin, $"/admin/pages?selected={about}", $"/admin/pages/delete/{about}", new Dictionary<string, string>());

        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.PageTranslations.AnyAsync(t => t.PageId == about));
            var copy = await db.Pages.SingleAsync(p => p.Slug == "about-copy");
            Assert.Equal("Ueber uns", (await db.PageTranslations.SingleAsync(t => t.PageId == copy.Id && t.LocaleId == german)).Title);
        });
    }
}

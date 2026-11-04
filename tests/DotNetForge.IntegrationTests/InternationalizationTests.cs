using System.Net;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>Locales and their rules, the screen, and the request locale (.docs/features/internationalization.md).</summary>
public sealed class InternationalizationTests
{
    private const string Screen = "/admin/internationalization";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string action, Dictionary<string, string>? fields = null) =>
        DotNetForgeWebFactory.PostFormAsync(client, Screen, action, fields ?? new Dictionary<string, string>());

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, string code, bool enabled = true, string name = "") =>
        PostAsync(client, Screen + "/add", new() { ["Code"] = code, ["DisplayName"] = name, ["Enabled"] = enabled ? "true" : "false" });

    private static async Task<List<Locale>> LocalesAsync(DotNetForgeWebFactory factory)
    {
        var locales = new List<Locale>();
        await factory.WithDbAsync(async db => locales = await db.Locales.AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync());
        return locales;
    }

    [Fact]
    public async Task A_new_site_has_english_as_its_only_and_default_locale()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();

        var locale = Assert.Single(await LocalesAsync(factory));
        Assert.Equal(("en", true, true), (locale.Code, locale.IsDefault, locale.Enabled));
    }

    [Fact]
    public async Task Adding_validates_the_code_and_stores_it_canonically()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        Assert.Equal(HttpStatusCode.Redirect, (await AddAsync(admin, "de-de")).StatusCode);
        var duplicate = await AddAsync(admin, "DE-DE");
        var malformed = await AddAsync(admin, "german!");

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode); // re-rendered with the error
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());
        Assert.Contains("not a valid locale code", await malformed.Content.ReadAsStringAsync());
        var added = Assert.Single(await LocalesAsync(factory), l => !l.IsDefault);
        Assert.Equal("de-DE", added.Code);
        Assert.StartsWith("Deutsch", added.DisplayName); // an empty display name is the language's own
        Assert.False(added.IsDefault);
    }

    [Fact]
    public async Task Setting_the_default_switches_atomically_and_enables_the_new_default()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await AddAsync(admin, "fr", enabled: false);
        var fr = (await LocalesAsync(factory)).Single(l => l.Code == "fr");

        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, $"{Screen}/{fr.Id}/default")).StatusCode);

        var locales = await LocalesAsync(factory);
        var only = Assert.Single(locales, l => l.IsDefault);
        Assert.Equal("fr", only.Code);
        Assert.True(only.Enabled);
        Assert.False(locales.Single(l => l.Code == "en").IsDefault);
        await factory.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.LocaleDefaultChanged && a.EntityId == fr.Id.ToString())));
    }

    [Fact]
    public async Task Concurrent_default_changes_leave_exactly_one_default()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await AddAsync(admin, "de");
        await AddAsync(admin, "it");
        var locales = await LocalesAsync(factory);
        var ids = locales.Where(l => !l.IsDefault).Select(l => l.Id).ToList();

        var tokenPage = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, Screen);
        await Task.WhenAll(ids.Select(id => admin.PostAsync($"{Screen}/{id}/default",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = tokenPage }))));

        Assert.Single(await LocalesAsync(factory), l => l.IsDefault);
    }

    [Fact]
    public async Task The_default_locale_can_be_neither_disabled_nor_deleted()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var en = Assert.Single(await LocalesAsync(factory));

        await PostAsync(admin, $"{Screen}/{en.Id}/disable");
        await PostAsync(admin, $"{Screen}/{en.Id}/delete");

        var still = Assert.Single(await LocalesAsync(factory));
        Assert.True(still.IsDefault && still.Enabled);
    }

    [Fact]
    public async Task Deleting_a_locale_deletes_its_translations_and_is_audited()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await AddAsync(admin, "nl");
        var nl = (await LocalesAsync(factory)).Single(l => l.Code == "nl");
        await factory.WithDbAsync(async db =>
        {
            var page = await db.Pages.OrderBy(p => p.Id).FirstAsync();
            db.PageTranslations.Add(new PageTranslation { PageId = page.Id, LocaleId = nl.Id, Title = "Welkom" });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(admin, $"{Screen}/{nl.Id}/delete")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Locales.AnyAsync(l => l.Id == nl.Id));
            Assert.False(await db.PageTranslations.AnyAsync(t => t.LocaleId == nl.Id));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.LocaleDeleted));
        });
    }

    [Fact]
    public async Task Only_users_with_the_settings_update_grant_change_locales()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Editor-Password-1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Editor-Password-1");

        // The token of a screen the editor may open: the add itself is what's refused.
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(editor, "/admin");
        var add = await editor.PostAsync(Screen + "/add", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Code"] = "es",
            ["Enabled"] = "true",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.False(add.StatusCode == HttpStatusCode.Redirect && add.Headers.Location?.OriginalString == Screen, "the editor added a locale");
        Assert.Single(await LocalesAsync(factory));
    }

    [Fact]
    public async Task Public_urls_carry_the_locale_prefix_and_the_html_language()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await AddAsync(admin, "de");
        await AddAsync(admin, "fr", enabled: false);
        using var visitor = factory.CreateNoRedirectClient();

        var home = await visitor.GetAsync("/");
        var german = await visitor.GetAsync("/de");
        var disabled = await visitor.GetAsync("/fr");
        var defaultPrefix = await visitor.GetAsync("/en/?x=1");

        Assert.Contains("<html lang=\"en\"", await home.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, german.StatusCode);
        Assert.Contains("<html lang=\"de\"", await german.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.MovedPermanently, defaultPrefix.StatusCode);
        Assert.Equal("/?x=1", defaultPrefix.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_locale_code_and_a_top_level_slug_never_share_a_url()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        // The seeded site has a top-level "about" page; a locale "about" isn't a valid code, so use a page named like one.
        await factory.WithDbAsync(async db =>
        {
            db.Pages.Add(new Page { Title = "Dansk", Slug = "da", Published = true });
            await db.SaveChangesAsync();
        });
        var refused = await AddAsync(admin, "da");
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("which this locale", System.Net.WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(await LocalesAsync(factory), l => l.Code == "da");
    }

    [Fact]
    public async Task The_admin_and_the_api_have_no_localized_urls()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await AddAsync(admin, "de");

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/de/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/de/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/de")).StatusCode);
    }

    [Fact]
    public async Task Locales_are_added_from_their_own_screen()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        // The header button leads to the add screen.
        Assert.Contains("href=\"/admin/internationalization/add\"", await admin.GetStringAsync(Screen));

        // A refused locale is shown on the add screen with what was entered.
        var refused = await DotNetForgeWebFactory.PostFormAsync(admin, Screen + "/add", Screen + "/add",
            new Dictionary<string, string> { ["Code"] = "german!", ["DisplayName"] = "", ["Enabled"] = "true" });
        var html = await refused.Content.ReadAsStringAsync();
        Assert.Contains("not a valid locale code", html);
        Assert.Contains("value=\"german!\"", html);
    }
}

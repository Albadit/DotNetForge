using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services.Localization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>The admin language: the site's setting, the user's own choice, and what the admin shows (.docs/features/internationalization.md#admin-language).</summary>
public sealed partial class AdminLanguageTests
{
    private static readonly AdminTranslations Translations = new();

    private static string German(string english) => Translations.Find("de")!.Strings[english];

    [Fact]
    public void German_Dutch_and_French_are_offered_after_English()
    {
        var codes = Translations.Languages.Select(l => l.Code).ToList();
        Assert.Equal("en", codes[0]);
        Assert.Contains("de", codes);
        Assert.Contains("nl", codes);
        Assert.Contains("fr", codes);
        Assert.True(Translations.Language("ar")?.RightToLeft ?? true, "Arabic, when offered, is right to left");
    }

    [Fact]
    public async Task The_site_language_shows_every_admin_user_the_admin_in_it()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var english = await admin.GetStringAsync("/admin/profile");
        Assert.Contains("<html lang=\"en\" dir=\"ltr\"", english);
        Assert.DoesNotContain("name=\"dnf-text\"", english);

        var saved = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", "/admin/internationalization/admin-language",
            new Dictionary<string, string> { ["language"] = "de" });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var german = await admin.GetStringAsync("/admin/profile");
        Assert.Contains("<html lang=\"de\" dir=\"ltr\"", german);
        Assert.Contains(German("Sign out"), german);
        Assert.Contains("name=\"dnf-text\"", german); // the scripts' text
        await factory.WithDbAsync(async db =>
        {
            Assert.Equal("de", (await db.Settings.SingleAsync(s => s.Key == AdminLanguageService.SettingKey)).Value);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.AdminLanguageChanged));
        });

        // The public site keeps its own locale.
        Assert.Contains("lang=\"en\"", await admin.GetStringAsync("/"));
    }

    [Fact]
    public async Task A_users_own_language_wins_and_empty_follows_the_site_again()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/profile", "/admin/profile/language", new Dictionary<string, string> { ["language"] = "fr" });
        Assert.Contains("<html lang=\"fr\"", await admin.GetStringAsync("/admin"));

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/profile", "/admin/profile/language", new Dictionary<string, string> { ["language"] = "" });
        Assert.Contains("<html lang=\"en\"", await admin.GetStringAsync("/admin"));
        await factory.WithDbAsync(async db => Assert.Null((await db.Users.SingleAsync()).AdminLanguage));
    }

    [Fact]
    public async Task A_language_without_a_catalog_is_refused()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/profile", "/admin/profile/language", new Dictionary<string, string> { ["language"] = "xx" });
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", "/admin/internationalization/admin-language",
            new Dictionary<string, string> { ["language"] = "klingon" });

        Assert.Contains("<html lang=\"en\"", await admin.GetStringAsync("/admin"));
        await factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.Users.SingleAsync()).AdminLanguage);
            Assert.False(await db.Settings.AnyAsync(s => s.Key == AdminLanguageService.SettingKey));
        });
    }

    /// <summary>
    /// Every admin screen in German shows no text that is still the English of a catalog key with a different German
    /// translation - which catches text shown through a variable that was never translated (an unmarked label).
    /// </summary>
    [Fact]
    public async Task Every_admin_screen_in_german_has_no_english_left()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", "/admin/internationalization/add",
            new Dictionary<string, string> { ["Code"] = "nl", ["DisplayName"] = "", ["Enabled"] = "true" });
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/internationalization", "/admin/internationalization/admin-language",
            new Dictionary<string, string> { ["language"] = "de" });

        var paths = new List<string>(AdminUiTests.AdminScreens)
        {
            "/admin/mfa", "/admin/media-settings", "/admin/api-endpoints", "/admin/api-endpoints/new", "/admin/api-docs",
            "/admin/webhooks/create", "/admin/profile/security",
        };
        await factory.WithDbAsync(async db =>
        {
            paths.Add($"/admin/pages?selected={await db.Pages.Select(p => p.Id).FirstAsync()}");
            paths.Add($"/admin/users/{await db.Users.Select(u => u.Id).FirstAsync()}");
            paths.AddRange(await db.Roles.Select(r => "/admin/roles/" + r.Id).ToListAsync());
            paths.AddRange(await db.Locales.Select(l => "/admin/internationalization/" + l.Id).ToListAsync());
            paths.AddRange(await db.ContentTypes.Select(t => "/admin/content/" + t.Slug).ToListAsync());
        });
        paths.Add("/admin/email/templates/" + DotNetForge.Core.Email.EmailTemplateCatalog.All[0].Key);

        var german = Translations.Find("de")!.Strings;
        var leftovers = new List<string>();
        foreach (var path in paths)
        {
            var response = await admin.GetAsync(path);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                leftovers.Add($"{path}: {(int)response.StatusCode}");
                continue;
            }

            foreach (var text in VisibleTexts(await response.Content.ReadAsStringAsync()))
            {
                if (german.TryGetValue(text, out var translated) && translated != text && !SharedWithData.Contains(text))
                {
                    leftovers.Add($"{path}: \"{text}\"");
                }
            }
        }

        Assert.True(leftovers.Count == 0, "English left on German screens:\n" + string.Join("\n", leftovers.Distinct()));
    }

    /// <summary>
    /// Texts that are also stored data, shown as stored: role names, the seeded pages, and the audit log's entity types
    /// and names ("User", "Locale", "Admin language").
    /// </summary>
    private static readonly HashSet<string> SharedWithData = new(StringComparer.Ordinal)
    {
        "Public", "Authenticated", "Home", "About", "History", "User", "Locale", "Admin language",
    };

    /// <summary>The text nodes and the title/aria-label/placeholder values of a page, decoded and trimmed.</summary>
    private static IEnumerable<string> VisibleTexts(string html)
    {
        var body = ScriptOrStyle().Replace(html, " ");
        foreach (Match m in TextNode().Matches(body))
        {
            var text = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (text.Length > 0)
            {
                yield return text;
            }
        }

        foreach (Match m in TextAttribute().Matches(body))
        {
            yield return WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        }
    }

    [GeneratedRegex(@"<(script|style|template|code|pre|textarea)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@">([^<>]+)<")]
    private static partial Regex TextNode();

    [GeneratedRegex(@"\s(?:title|aria-label|placeholder)=""([^""]*)""")]
    private static partial Regex TextAttribute();
}

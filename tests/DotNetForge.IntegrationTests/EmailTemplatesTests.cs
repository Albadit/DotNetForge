using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Core.Email;
using DotNetForge.Shared.Constants;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Email → Templates (.docs/pages/email-templates.md): customizing, validating, previewing and restoring templates,
/// and the emails the CMS sends with them (reset, welcome, account locked).
/// </summary>
public sealed class EmailTemplatesTests
{
    private const string Templates = "/admin/email/templates";
    private const string Reset = Templates + "/" + EmailTemplateCatalog.PasswordReset;

    [Fact]
    public async Task A_customized_template_is_sent_and_restoring_brings_back_the_default()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var list = await admin.GetStringAsync(Templates);
        foreach (var t in EmailTemplateCatalog.All)
        {
            Assert.Contains(t.Name, list);
        }

        var saved = await SaveAsync(admin, "Your {{appName}} reset", "<p>Custom for {{userName}}: <a href=\"{{resetUrl}}\">go</a></p>");
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("Customized", await admin.GetStringAsync(Reset));
        await factory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.SettingsChanged && a.EntityType == "EmailTemplate")));

        // The reset email uses it (values filled in, the link included).
        using var visitor = factory.CreateNoRedirectClient();
        await DotNetForgeWebFactory.PostFormAsync(visitor, "/forgot-password", "/forgot-password",
            new Dictionary<string, string> { ["email"] = DotNetForgeWebFactory.AdminEmail });
        var email = Assert.Single(factory.SentEmails);
        Assert.Equal("Your DotNetForge CMS reset", email.Subject);
        Assert.Contains("Custom for admin@example.com:", email.Body);
        Assert.Contains("http://localhost/reset-password?token=", email.Body);

        var restored = await DotNetForgeWebFactory.PostFormAsync(admin, Reset, Reset + "/restore", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, restored.StatusCode);
        await factory.WithDbAsync(async db => Assert.False(await db.EmailTemplates.AnyAsync()));
        Assert.Contains("Default", await admin.GetStringAsync(Reset));
    }

    [Fact]
    public async Task Unknown_or_missing_variables_and_stale_saves_are_refused()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var unknown = await SaveAsync(admin, "Hi {{firstName}}", "<p>no link</p>");
        var html = WebUtility.HtmlDecode(await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Contains("Unknown variable {{firstName}} in the subject", html);
        Assert.Contains("The body must contain {{resetUrl}}", html);

        // Two editors open the default; the second save is based on a version that no longer exists.
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(admin, "First {{appName}}", "{{resetUrl}}", version: "0")).StatusCode);
        var stale = await SaveAsync(admin, "Second", "{{resetUrl}}", version: "0");
        Assert.Contains("Someone else changed this template", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        await factory.WithDbAsync(async db => Assert.Equal("First {{appName}}", (await db.EmailTemplates.SingleAsync()).Subject));
    }

    [Fact]
    public async Task The_preview_renders_sample_data_in_a_sandbox()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var saved = await admin.GetAsync(Reset + "/preview");
        var csp = string.Join(" ", saved.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("sandbox", csp);
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("Alex Example", await saved.Content.ReadAsStringAsync());
        // The editor page loads the self-hosted Monaco editor and allows its runtime styles - only there.
        var editor = await admin.GetAsync(Reset);
        var editorHtml = await editor.Content.ReadAsStringAsync();
        Assert.Contains("<iframe id=\"email-preview\" name=\"email-preview\"", editorHtml);
        Assert.Contains("data-preview-refresh", editorHtml);
        Assert.Contains("data-status-wrap", editorHtml);
        Assert.Contains("data-max=\"20000\"", editorHtml);
        Assert.Contains("src=\"/lib/monaco/vs/loader.js\"", editorHtml);
        Assert.Contains("data-template-variables=", editorHtml);
        Assert.Contains("style-src 'self' 'unsafe-inline'", string.Join(" ", editor.Headers.GetValues("Content-Security-Policy-Report-Only")));
        Assert.DoesNotContain("unsafe-inline", string.Join(" ", (await admin.GetAsync(Templates)).Headers.GetValues("Content-Security-Policy-Report-Only")));
        Assert.Equal(System.Net.HttpStatusCode.OK, (await admin.GetAsync("/lib/monaco/vs/editor/editor.main.js")).StatusCode);

        // Unsaved text: rendered, or the errors instead of a broken render. Nothing is stored.
        var changed = await DotNetForgeWebFactory.PostFormAsync(admin, Reset, Reset + "/preview",
            new Dictionary<string, string> { ["subject"] = "S", ["body"] = "<p>Draft {{expiryMinutes}} {{resetUrl}}</p>" });
        Assert.Contains("<p>Draft 60 https://cms.example.com/reset-password?token=sample</p>", await changed.Content.ReadAsStringAsync());
        var broken = await DotNetForgeWebFactory.PostFormAsync(admin, Reset, Reset + "/preview",
            new Dictionary<string, string> { ["subject"] = "S", ["body"] = "{{nope}}" });
        Assert.Contains("can't be rendered", await broken.Content.ReadAsStringAsync());
        await factory.WithDbAsync(async db => Assert.False(await db.EmailTemplates.AnyAsync()));
    }

    [Fact]
    public async Task Registering_sends_a_welcome_email_and_a_lockout_sends_account_locked()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/advanced-settings", "/admin/advanced-settings", new Dictionary<string, string>
        {
            ["SignUps"] = "true",
            ["EmailConfirmation"] = "false",
            ["ResetPasswordUrl"] = string.Empty,
            ["ConfirmationRedirectUrl"] = string.Empty,
        });

        using var visitor = factory.CreateNoRedirectClient();
        await DotNetForgeWebFactory.PostFormAsync(visitor, "/register", "/register", new Dictionary<string, string>
        {
            ["Email"] = "new@example.com",
            ["Password"] = "N3wPassword!",
            ["ConfirmPassword"] = "N3wPassword!",
        });
        var welcome = Assert.Single(factory.SentEmails);
        Assert.Equal("new@example.com", welcome.To);
        Assert.Equal("Welcome to DotNetForge CMS", welcome.Subject);

        using var attacker = factory.CreateNoRedirectClient();
        for (var i = 0; i < 5; i++)
        {
            await DotNetForgeWebFactory.PostFormAsync(attacker, "/login", "/login",
                new Dictionary<string, string> { ["email"] = "new@example.com", ["password"] = "wrong-" + i });
        }

        var locked = factory.SentEmails.Last();
        Assert.Equal(2, factory.SentEmails.Count);
        Assert.Contains("is locked", locked.Subject);
        Assert.Matches(new Regex("locked for 15 minutes"), locked.Body);
        Assert.Contains("http://localhost/forgot-password", locked.Body);
    }

    [Fact]
    public async Task Only_the_settings_permission_opens_templates()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Ed1torPassword!", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Ed1torPassword!");
        Assert.Contains("/account/denied", (await editor.GetAsync(Templates)).Headers.Location?.OriginalString);
        Assert.Contains("/account/denied", (await editor.GetAsync(Reset + "/preview")).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await (await factory.SignInAsync()).GetAsync(Templates + "/nope")).StatusCode);
    }

    /// <summary>Saves the password-reset template; without <paramref name="version"/> the one the editor shows now.</summary>
    private static async Task<HttpResponseMessage> SaveAsync(HttpClient admin, string subject, string body, string? version = null)
    {
        if (version is null)
        {
            var page = await admin.GetStringAsync(Reset);
            version = Regex.Match(page, "name=\"version\" value=\"(\\d+)\"").Groups[1].Value;
        }

        return await DotNetForgeWebFactory.PostFormAsync(admin, Reset, Reset, new Dictionary<string, string>
        {
            ["subject"] = subject,
            ["body"] = body,
            ["version"] = version,
        });
    }
}

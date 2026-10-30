using System.Net;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The extension host end to end (.docs/features/extensions.md): code loading, enable/disable, every slot. Uses the
/// fixture extensions in <c>tests/fixtures/extensions</c> (built before the tests, loaded from their own bin/ like
/// any extension) through <c>EXTENSIONS_PATH</c>.
/// </summary>
public sealed class ExtensionTests
{
    private static readonly string FixturesPath = Path.Combine(
        AppPaths.FindRepositoryRoot(AppContext.BaseDirectory)!, "tests", "fixtures", "extensions");

    private static async Task<(DotNetForgeWebFactory Factory, HttpClient Admin)> StartAsync()
    {
        var factory = new DotNetForgeWebFactory(extensionsPath: FixturesPath);
        await factory.InstallAsync();
        return (factory, await factory.SignInAsync());
    }

    private static Task<HttpResponseMessage> EnableAsync(HttpClient admin, string id) =>
        DotNetForgeWebFactory.PostFormAsync(admin, "/admin/plugins", $"/admin/plugins/{id}/enable", new Dictionary<string, string>());

    private static Task<HttpResponseMessage> DisableAsync(HttpClient admin, string id) =>
        DotNetForgeWebFactory.PostFormAsync(admin, "/admin/plugins", $"/admin/plugins/{id}/disable", new Dictionary<string, string>());

    private static async Task<string> PluginsMessageAsync(HttpClient admin) =>
        WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/plugins"));

    [Fact]
    public async Task Plugins_lists_every_extension_disabled_and_explains_what_cannot_run()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;

        var html = await PluginsMessageAsync(admin);

        Assert.Contains("Test Kit", html);
        Assert.Contains("tests.widget.hello", html);
        Assert.Contains("'bin/Missing.dll' was not found", html);
        Assert.DoesNotContain("data-enabled=\"true\"", html);
        Assert.Equal(HttpStatusCode.Redirect, (await EnableAsync(admin, "tests.plugin.broken")).StatusCode);
        Assert.Contains("can't be enabled", await PluginsMessageAsync(admin));
    }

    [Fact]
    public async Task Extension_endpoints_and_controllers_answer_only_while_enabled()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;
        var anonymous = factory.CreateNoRedirectClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/test-kit/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/test-kit/controller")).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await EnableAsync(admin, "tests.plugin.kit")).StatusCode);
        Assert.Equal("pong", await anonymous.GetStringAsync("/test-kit/ping")); // settings reach the code
        Assert.Equal("controller-ok", await anonymous.GetStringAsync("/test-kit/controller"));

        Assert.Equal(HttpStatusCode.Redirect, (await DisableAsync(admin, "tests.plugin.kit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/test-kit/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/test-kit/controller")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            var actions = await db.AuditLogs.Where(a => a.EntityId == "tests.plugin.kit").Select(a => a.Action).ToListAsync();
            Assert.Equal(new[] { AuditActions.PluginEnabled, AuditActions.PluginDisabled }, actions);
            Assert.Equal(ExtensionStatus.Disabled, (await db.InstalledExtensions.SingleAsync(x => x.Id == "tests.plugin.kit")).Status);
        });
    }

    [Fact]
    public async Task Enabling_needs_enabled_dependencies_and_disabling_needs_no_enabled_dependents()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;

        await EnableAsync(admin, "tests.plugin.dependent");
        Assert.Contains("needs tests.library.base - enable it first", await PluginsMessageAsync(admin));

        await EnableAsync(admin, "tests.library.base");
        await EnableAsync(admin, "tests.plugin.dependent");
        await DisableAsync(admin, "tests.library.base");
        Assert.Contains("depends on tests.library.base - disable it first", await PluginsMessageAsync(admin));

        await factory.WithDbAsync(async db => Assert.Equal(2,
            await db.InstalledExtensions.CountAsync(x => x.Status == ExtensionStatus.Enabled)));
    }

    [Fact]
    public async Task Widgets_render_on_the_dashboard_and_admin_tabs_in_the_admin_area()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;
        Assert.DoesNotContain("WIDGET-OK", await admin.GetStringAsync("/admin"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/admin/ext/tests.admin.hello/raw")).StatusCode);

        await EnableAsync(admin, "tests.widget.hello");
        await EnableAsync(admin, "tests.admin.hello");

        var dashboard = await admin.GetStringAsync("/admin");
        Assert.Contains("WIDGET-OK", dashboard);
        Assert.Contains("href=\"/admin/ext/tests.admin.hello\"", dashboard); // sidebar tab
        Assert.Contains("ADMIN-OK", await admin.GetStringAsync("/admin/ext/tests.admin.hello/raw"));

        // A widget's resources are served inside the admin area only, never on the public site.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/ext/tests.widget.hello/resources/widget.css")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/ext/tests.widget.hello/resources/widget.css")).StatusCode);
    }

    [Fact]
    public async Task Themes_render_public_pages_and_modules_appear_on_their_pages()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;
        var visitor = factory.CreateNoRedirectClient();
        Assert.DoesNotContain("THEME-OK", await visitor.GetStringAsync("/about"));

        await EnableAsync(admin, "tests.theme.plain");
        await EnableAsync(admin, "tests.module.hello");

        var about = await visitor.GetStringAsync("/about");
        Assert.Contains("THEME-OK About", about);
        Assert.Contains("MODULE-OK on About", about); // pages: ["about"]
        var news = await visitor.GetStringAsync("/news");
        Assert.Contains("THEME-OK News", news);
        Assert.DoesNotContain("MODULE-OK", news);

        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/ext/tests.theme.plain/resources/theme.css")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await visitor.GetAsync("/ext/tests.theme.plain/resources/..%2F..%2Fdotnetforge.extension.json")).StatusCode);

        await DisableAsync(admin, "tests.theme.plain");
        Assert.DoesNotContain("THEME-OK", await visitor.GetStringAsync("/about"));
        Assert.Contains("MODULE-OK", await visitor.GetStringAsync("/about")); // the built-in page view hosts modules too
    }

    [Fact]
    public async Task A_storage_connector_takes_over_media_while_enabled()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;
        await EnableAsync(admin, "tests.plugin.kit");

        Assert.Equal(HttpStatusCode.Redirect, (await MediaTests.UploadAsync(admin, "a.png", new byte[] { 1, 2, 3 }, isPublic: true)).StatusCode);
        Assert.False(Directory.Exists(factory.StoragePath) && Directory.EnumerateFiles(factory.StoragePath, "*", SearchOption.AllDirectories).Any());
        Assert.Contains(".png", await admin.GetStringAsync("/test-kit/storage"));

        await DisableAsync(admin, "tests.plugin.kit");
        Assert.Equal(HttpStatusCode.Redirect, (await MediaTests.UploadAsync(admin, "b.png", new byte[] { 4, 5, 6 }, isPublic: true)).StatusCode);
        Assert.Single(Directory.EnumerateFiles(factory.StoragePath, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Audit_events_reach_enabled_extension_handlers()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;
        await EnableAsync(admin, "tests.plugin.kit");

        var failed = factory.CreateNoRedirectClient();
        await DotNetForgeWebFactory.PostFormAsync(failed, "/login", "/login",
            new Dictionary<string, string> { ["email"] = "nobody@example.com", ["password"] = "wrong" });

        // Handlers run in the background: wait (bounded) for the event.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        string events;
        do
        {
            events = await admin.GetStringAsync("/test-kit/events");
        }
        while (!events.Contains(AuditActions.UserLoginFailed) && DateTime.UtcNow < deadline && await Delay());

        Assert.Contains(AuditActions.UserLoginFailed, events);
    }

    [Fact]
    public async Task Authentication_extensions_sign_in_existing_users_by_email()
    {
        var (factory, admin) = await StartAsync();
        using var _ = factory;
        var visitor = factory.CreateNoRedirectClient();
        Assert.DoesNotContain("Sign in with Test Kit", await visitor.GetStringAsync("/login"));

        await EnableAsync(admin, "tests.plugin.kit");
        Assert.Contains("Sign in with Test Kit", await visitor.GetStringAsync("/login"));

        // Unknown email: back to the sign-in screen with a message, nothing signed in.
        var unknown = await ExternalSignInAsync(visitor, "stranger@example.com");
        Assert.Contains("/login", unknown.Headers.Location?.OriginalString);
        Assert.Contains("No account here uses the email", WebUtility.HtmlDecode(await visitor.GetStringAsync("/login")));
        Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync("/admin")).StatusCode);

        var known = await ExternalSignInAsync(visitor, DotNetForgeWebFactory.AdminEmail);
        Assert.Equal("/admin", known.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/admin")).StatusCode);

        await factory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.Action == AuditActions.UserLogin && a.Details == "via Test Kit")));
    }

    /// <summary>
    /// Clicks "Sign in with Test Kit" and follows the provider round trip to the host callback. The button's post is
    /// answered with the "Continuing to ..." page, whose meta refresh goes to the provider (form-action 'self').
    /// </summary>
    private static async Task<HttpResponseMessage> ExternalSignInAsync(HttpClient client, string email)
    {
        var challenge = await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/account/external/testkit",
            new Dictionary<string, string> { ["email"] = email });
        Assert.Equal(HttpStatusCode.OK, challenge.StatusCode);
        var page = await challenge.Content.ReadAsStringAsync();
        var refresh = System.Text.RegularExpressions.Regex.Match(page, "http-equiv=\"refresh\" content=\"0;url=([^\"]+)\"");
        Assert.True(refresh.Success, "The continue page has no meta refresh.");
        return await client.GetAsync(WebUtility.HtmlDecode(refresh.Groups[1].Value));
    }

    private static async Task<bool> Delay()
    {
        await Task.Delay(50);
        return true;
    }
}

/// <summary>
/// The sample extensions in <c>extensions/</c> working together: the Contact Form module uses the Markdown Kit library
/// (shared across load contexts, also inside its runtime-compiled view), the SEO Toolkit plugin serves the sitemap.
/// </summary>
public sealed class SampleExtensionTests
{
    [Fact]
    public async Task Contact_form_module_renders_with_its_library_and_stores_messages()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await factory.WithDbAsync(async db =>
        {
            var contact = await db.Pages.SingleAsync(p => p.Slug == "contact");
            contact.Published = true;
            await db.SaveChangesAsync();
        });
        foreach (var id in new[] { "dotnetforge.library.markdown-kit", "dotnetforge.module.contact-form" })
        {
            await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/plugins", $"/admin/plugins/{id}/enable", new Dictionary<string, string>());
        }

        var visitor = factory.CreateNoRedirectClient();
        var page = await visitor.GetStringAsync("/contact");
        Assert.Contains("<strong>project</strong>", page); // Markdown Kit rendered the intro

        var sent = await DotNetForgeWebFactory.PostFormAsync(visitor, "/contact", "/ext/contact-form/submit", new Dictionary<string, string>
        {
            ["returnPath"] = "/contact",
            ["name"] = "Ana",
            ["email"] = "ana@example.com",
            ["message"] = "Hello <b>there</b>",
        });
        Assert.Equal("/contact?contact-sent=1#contact-form", sent.Headers.Location?.OriginalString);

        var stored = Assert.Single(Directory.EnumerateFiles(Path.Combine(factory.StoragePath, "ext", "contact-form"), "*.json",
            SearchOption.AllDirectories));
        using var message = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(stored));
        Assert.Equal("Hello <b>there</b>", message.RootElement.GetProperty("message").GetString());
        await factory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "contactform.submitted")));
    }

    [Fact]
    public async Task Seo_toolkit_serves_a_sitemap_of_live_pages_while_enabled()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var visitor = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/sitemap.xml")).StatusCode);

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/plugins", "/admin/plugins/dotnetforge.plugin.seo-toolkit/enable",
            new Dictionary<string, string>());

        var sitemap = await visitor.GetStringAsync("/sitemap.xml");
        Assert.Contains("<loc>http://localhost/about/team</loc>", sitemap);
        Assert.DoesNotContain("/contact</loc>", sitemap); // not published
        Assert.DoesNotContain("history", sitemap); // not published
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", await visitor.GetStringAsync("/robots.txt"));
    }
}

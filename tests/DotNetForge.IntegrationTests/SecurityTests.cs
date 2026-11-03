using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNetForge.Abstractions.Security;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>Regression tests for the security fixes listed in .docs/features/security.md.</summary>
public sealed class SecurityTests
{
    [Fact]
    public async Task Responses_carry_security_headers()
    {
        using var factory = new DotNetForgeWebFactory("Production");
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var response = await client.GetAsync("/login");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task An_exception_during_a_post_renders_the_error_page()
    {
        // Regression: /error used to be GET-only, so a failed form POST returned an empty 500.
        using var baseFactory = new DotNetForgeWebFactory("Production");
        await baseFactory.InstallAsync();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IPageService, ThrowingPageService>()));
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/login", new Dictionary<string, string>
        {
            ["email"] = DotNetForgeWebFactory.AdminEmail,
            ["password"] = DotNetForgeWebFactory.AdminPassword,
        });
        Guid aboutId = default;
        await baseFactory.WithDbAsync(async db => aboutId = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync());

        var response = await DotNetForgeWebFactory.PostFormAsync(client, $"/admin/pages?selected={aboutId}",
            $"/admin/pages/update/{aboutId}", new Dictionary<string, string> { ["Title"] = "About", ["Slug"] = "about" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Something went wrong", await response.Content.ReadAsStringAsync());
    }

    private sealed class ThrowingPageService : IPageService
    {
        public Task<string?> ApplyAsync(Page page, PageInput input, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure.");

        public Task<string?> ReorderAsync(IReadOnlyList<PagePosition> positions, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure.");

        public Task<string?> MoveAsync(Guid pageId, Guid? parentPageId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure.");

        public Task<string?> DeleteAsync(Page page, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated failure.");
    }

    [Fact]
    public async Task Login_ignores_an_external_return_url()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var response = await DotNetForgeWebFactory.PostFormAsync(client, "/login",
            "/login?returnUrl=https%3A%2F%2Fevil.example%2F",
            new Dictionary<string, string>
            {
                ["email"] = DotNetForgeWebFactory.AdminEmail,
                ["password"] = DotNetForgeWebFactory.AdminPassword,
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Successful_login_is_audited_with_the_user()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.SignInAsync();

        await factory.WithDbAsync(async db =>
        {
            var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.UserLogin);
            Assert.NotNull(entry.UserId);
        });
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_session()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var editorId = await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/admin/pages")).StatusCode);

        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == editorId);
            user.Status = UserStatus.Disabled;
            await db.SaveChangesAsync();
        });

        // The "still active" check is cached for up to 30 s per user; a fresh factory cache isn't shared, so clear it.
        factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove($"dnf:session:{editorId}");

        var response = await editor.GetAsync("/admin/pages");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Repeated_sign_in_attempts_are_rate_limited()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/login");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "nobody@example.com",
                ["password"] = "wrong-password-1",
                ["__RequestVerificationToken"] = token,
            }));
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Editors_can_no_longer_change_settings()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");

        var response = await editor.GetAsync("/admin/settings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/denied", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Authors_cannot_delete_or_publish_pages_they_did_not_create()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");
        Guid aboutId = default;
        await factory.WithDbAsync(async db => aboutId = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync());

        var delete = await DotNetForgeWebFactory.PostFormAsync(author, "/admin/pages", $"/admin/pages/delete/{aboutId}",
            new Dictionary<string, string>());
        Assert.Contains("/account/denied", delete.Headers.Location?.OriginalString);

        // Their own page: editable, but publishing needs the publish permission.
        await DotNetForgeWebFactory.PostFormAsync(author, "/admin/pages", "/admin/pages/create", new Dictionary<string, string>());
        Guid ownId = default;
        await factory.WithDbAsync(async db => ownId = await db.Pages.Where(p => p.Title == "Untitled page").Select(p => p.Id).SingleAsync());

        var publish = await DotNetForgeWebFactory.PostFormAsync(author, $"/admin/pages?selected={ownId}",
            $"/admin/pages/update/{ownId}", new Dictionary<string, string>
            {
                ["Title"] = "Mine",
                ["Slug"] = "mine",
                ["PageType"] = "Standard",
                ["Published"] = "true",
            });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);
        Assert.Contains("permission to publish", await publish.Content.ReadAsStringAsync());

        var save = await DotNetForgeWebFactory.PostFormAsync(author, $"/admin/pages?selected={ownId}",
            $"/admin/pages/update/{ownId}", new Dictionary<string, string>
            {
                ["Title"] = "Mine",
                ["Slug"] = "mine",
                ["PageType"] = "Standard",
            });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
    }

    [Fact]
    public async Task Reorder_rejects_a_cycle()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        Guid aboutId = default, teamId = default;
        await factory.WithDbAsync(async db =>
        {
            aboutId = await db.Pages.Where(p => p.Slug == "about").Select(p => p.Id).SingleAsync();
            teamId = await db.Pages.Where(p => p.Slug == "team").Select(p => p.Id).SingleAsync();
        });
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, "/admin/pages");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/pages/reorder")
        {
            // About becomes a child of its own child Team.
            Content = JsonContent.Create(new { items = new[] { new { id = aboutId, parentPageId = (Guid?)teamId, sortOrder = 0 } } }),
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("cycle", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Duplicate_api_token_name_is_a_validation_error_not_a_crash()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var fields = new Dictionary<string, string> { ["name"] = "ci", ["duration"] = "7", ["permissions"] = "content.read" };

        var first = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-tokens/create", "/admin/api-tokens/create", fields);
        var second = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-tokens/create", "/admin/api-tokens/create", fields);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("already exists", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_page_creation_applies_the_content_rules_and_is_audited()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        string plaintext = string.Empty;
        await factory.WithDbAsync(async db =>
        {
            var generated = factory.Services.GetRequiredService<IApiTokenFactory>().Generate();
            plaintext = generated.Plaintext;
            db.ApiTokens.Add(new ApiToken
            {
                Name = "test",
                PermissionsCsv = PermissionKeys.ContentCreate,
                TokenHash = generated.Hash,
                TokenPrefix = generated.Prefix,
                ExpirationDate = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        });

        using var client = factory.CreateNoRedirectClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        var created = await client.PostAsJsonAsync("/api/content/pages", new { title = "Pricing", slug = "Our Pricing!" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("our-pricing", await created.Content.ReadAsStringAsync()); // slugified like the admin form

        var duplicate = await client.PostAsJsonAsync("/api/content/pages", new { title = "About", slug = "about" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode); // previously accepted at root level

        await factory.WithDbAsync(async db =>
        {
            var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.ContentCreated);
            Assert.Null(entry.UserId); // the actor is the token, not a user
            Assert.StartsWith("API token", entry.UserDisplaySnapshot);
        });
    }

    [Fact]
    public async Task A_signed_in_super_admin_calls_the_api_from_the_admin_without_a_token()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("admin2@example.com", "Adm1nPassword!", Roles.Admin);
        var superAdmin = await factory.SignInAsync();
        var admin = await factory.SignInAsync("admin2@example.com", "Adm1nPassword!");

        static HttpRequestMessage Get(string fetchSite, string? authorization = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/content/pages");
            if (fetchSite.Length > 0)
            {
                request.Headers.Add("Sec-Fetch-Site", fetchSite);
            }

            if (authorization is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }

            return request;
        }

        // Same-origin (the API docs' Send), also with the bare "Bearer" an empty token field sends.
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.SendAsync(Get("same-origin"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superAdmin.SendAsync(Get("same-origin", "Bearer "))).StatusCode);
        // Not from this origin (another site, the address bar, a non-browser client): the session doesn't count.
        Assert.Equal(HttpStatusCode.Unauthorized, (await superAdmin.SendAsync(Get("cross-site"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await superAdmin.SendAsync(Get("none"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await superAdmin.SendAsync(Get(""))).StatusCode);
        // Only Super Admins; everyone else uses a token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.SendAsync(Get("same-origin"))).StatusCode);

        // A token wins over the session, with its own permissions only.
        string plaintext = string.Empty;
        await factory.WithDbAsync(async db =>
        {
            var generated = factory.Services.GetRequiredService<IApiTokenFactory>().Generate();
            plaintext = generated.Plaintext;
            db.ApiTokens.Add(new ApiToken
            {
                Name = "limited",
                PermissionsCsv = PermissionKeys.MediaRead,
                TokenHash = generated.Hash,
                TokenPrefix = generated.Prefix,
                ExpirationDate = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await superAdmin.SendAsync(Get("same-origin", "Bearer " + plaintext))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await superAdmin.SendAsync(Get("same-origin", "Bearer wrong"))).StatusCode);

        // A write through the session is audited as the user.
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/content/pages") { Content = JsonContent.Create(new { title = "Session page", slug = "session-page" }) };
        create.Headers.Add("Sec-Fetch-Site", "same-origin");
        Assert.Equal(HttpStatusCode.Created, (await superAdmin.SendAsync(create)).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.ContentCreated);
            Assert.NotNull(entry.UserId);
            Assert.DoesNotContain("API token", entry.UserDisplaySnapshot ?? string.Empty);
        });
    }
}

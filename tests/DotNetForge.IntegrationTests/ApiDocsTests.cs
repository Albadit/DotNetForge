using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Shared.Constants;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The API docs (.docs/features/api-docs.md): Scalar and the OpenAPI document for Super Admins and Developers, the
/// custom endpoints in the document with their tokens filled in, the Scalar page's own Content-Security-Policy, and
/// what the Developer role opens.
/// </summary>
public sealed class ApiDocsTests
{
    private const string Docs = "/admin/api-docs";
    private const string Reference = "/admin/api-docs/reference/";
    private const string OpenApi = "/admin/api-docs/openapi/v1.json";

    private static string? Csp(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Content-Security-Policy", out var values) ||
        response.Headers.TryGetValues("Content-Security-Policy-Report-Only", out values)
            ? values.Single()
            : null;

    [Fact]
    public async Task Only_super_admins_and_developers_open_the_docs()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        await factory.CreateUserAsync("dev@example.com", "Dev3loperPass1", Roles.Developer);

        var anonymous = factory.CreateNoRedirectClient();
        foreach (var path in new[] { Docs, Reference, OpenApi })
        {
            Assert.Contains("/account/login", (await anonymous.GetAsync(path)).Headers.Location?.OriginalString);
        }

        var editor = await factory.SignInAsync("editor@example.com", "Edit0rPass1");
        foreach (var path in new[] { Docs, Reference, OpenApi })
        {
            Assert.Contains("/account/denied", (await editor.GetAsync(path)).Headers.Location?.OriginalString);
        }

        Assert.DoesNotContain($"href=\"{Docs}\"", await editor.GetStringAsync("/admin"));

        // A Developer sees the API screens - docs, tokens, endpoints - and nothing else of the admin.
        var developer = await factory.SignInAsync("dev@example.com", "Dev3loperPass1");
        var dashboard = await developer.GetStringAsync("/admin");
        foreach (var href in new[] { Docs, "/admin/api-tokens", "/admin/api-endpoints" })
        {
            Assert.Contains($"href=\"{href}\"", dashboard);
        }

        foreach (var href in new[] { "/admin/content", "/admin/pages", "/admin/content-types", "/admin/media", "/admin/users", "/admin/settings", "/admin/plugins" })
        {
            Assert.DoesNotContain($"href=\"{href}\"", dashboard);
        }

        Assert.Contains($"src=\"{Reference}\"", await developer.GetStringAsync(Docs));
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync("/admin/api-tokens/create")).StatusCode);
        Assert.Contains("/account/denied", (await developer.GetAsync("/admin/users")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_document_lists_the_api_and_the_custom_endpoints_with_their_tokens()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        foreach (var (name, path, access) in new[] { ("Items", "items", "Token"), ("Open items", "open", "Public") })
        {
            // "Items" has two operations: a list and a delete by id.
            var operations = new List<object> { new { method = "GET", path = "", sql = "SELECT 1" } };
            if (path == "items")
            {
                operations.Add(new { method = "DELETE", path = "{id}", sql = "SELECT @id", parameters = new[] { new { name = "id", source = "route" } }, result = "AffectedRows" });
            }

            var created = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/api-endpoints/new", "/admin/api-endpoints/new", new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Path"] = path,
                ["Access"] = access,
                ["Enabled"] = "true",
                ["OperationsJson"] = JsonSerializer.Serialize(operations),
            });
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        }

        var document = JsonDocument.Parse(await admin.GetStringAsync(OpenApi)).RootElement;
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/entries/{type}", out _));
        Assert.True(paths.TryGetProperty("/api/content/pages", out _));
        Assert.DoesNotContain(paths.EnumerateObject(), p => p.Name.StartsWith("/admin", StringComparison.Ordinal) || p.Name.Contains("{**", StringComparison.Ordinal));

        // The headless API: API-token scheme and the permission each operation needs.
        var schemes = document.GetProperty("components").GetProperty("securitySchemes");
        Assert.True(schemes.TryGetProperty("ApiToken", out _));
        var listEntries = paths.GetProperty("/api/entries/{type}").GetProperty("get");
        Assert.Contains("entries.read", listEntries.GetProperty("description").GetString());
        Assert.True(listEntries.GetProperty("security")[0].TryGetProperty("ApiToken", out _));

        // Custom endpoints: a token endpoint has its own scheme, a public one none.
        var items = paths.GetProperty("/api/custom/items").GetProperty("get");
        var scheme = items.GetProperty("security")[0].EnumerateObject().Single().Name;
        Assert.StartsWith("Endpoint ", scheme);
        Assert.True(schemes.TryGetProperty(scheme, out _));
        var delete = paths.GetProperty("/api/custom/items/{id}").GetProperty("delete");
        Assert.Equal(scheme, delete.GetProperty("security")[0].EnumerateObject().Single().Name); // one token for the whole endpoint
        Assert.Equal("Items", delete.GetProperty("tags")[0].GetString());
        Assert.False(paths.GetProperty("/api/custom/open").GetProperty("get").TryGetProperty("security", out _));

        // Scalar fills that endpoint's token in, and its inline script runs only with this response's nonce.
        var page = await admin.GetAsync(Reference);
        var html = await page.Content.ReadAsStringAsync();
        // The list is ordered by path, so the first editor link is "items".
        var editor = Regex.Match(await admin.GetStringAsync("/admin/api-endpoints"), "href=\"(/admin/api-endpoints/[0-9a-f-]{36})\"").Groups[1].Value;
        var token = Regex.Match(await admin.GetStringAsync(editor), "Bearer (dnfe_[A-Za-z0-9_-]+)").Groups[1].Value;
        Assert.StartsWith("dnfe_", token);
        Assert.Contains(token, html);
        var nonce = Regex.Match(html, "nonce=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(nonce);
        Assert.Contains($"script-src 'self' 'nonce-{nonce}'", Csp(page));

        // Every other page keeps the strict policy.
        Assert.Contains("script-src 'self';", Csp(await admin.GetAsync(Docs)));
    }
}

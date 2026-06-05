using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// End-to-end checks over the real host: install detection, the setup gate, admin auth gating, and
/// API token enforcement (installation_setup.md, admin_area.md, api_tokens.md).
/// </summary>
public sealed class CmsIntegrationTests
{
    private static HttpClient NoRedirectClient(DotNetForgeWebFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Health_endpoint_is_ok_even_before_install()
    {
        using var factory = new DotNetForgeWebFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Before_install_all_requests_redirect_to_setup()
    {
        using var factory = new DotNetForgeWebFactory();
        using var client = NoRedirectClient(factory);

        var root = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/setup", root.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Setup_page_is_served_before_install()
    {
        using var factory = new DotNetForgeWebFactory();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/setup");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email", html);
    }

    [Fact]
    public async Task After_install_setup_is_blocked()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/setup");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task After_install_admin_api_requires_authentication()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        // The admin SPA shell at /admin is a static file; the auth gate is the cookie-authenticated
        // admin API, which returns 401 (not a redirect) for the SPA to handle.
        var response = await client.GetAsync("/admin-api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task After_install_legacy_admin_requires_authentication()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/admin-legacy");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Api_requires_a_token()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = NoRedirectClient(factory);

        var response = await client.GetAsync("/api/content/pages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

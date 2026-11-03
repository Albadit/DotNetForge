using System.Net;
using DotNetForge.Web.Services;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The navigation warm-up and hover prefetch (.docs/architecture/pages.md#navigation): the admin layout names the app's
/// start, and a screen loaded in the background renders fully but leaves the flash message for the screen opened next.
/// </summary>
public sealed class NavigationWarmUpTests
{
    private const string Saved = "Automatic backups are off.";

    [Fact]
    public async Task A_background_load_leaves_the_toast_for_the_next_screen()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var client = await factory.SignInAsync();

        var saved = await DotNetForgeWebFactory.PostFormAsync(client, "/admin/data", "/admin/data/backups/schedule",
            new Dictionary<string, string> { ["enabled"] = "false", ["intervalHours"] = "24", ["keep"] = "7" });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        // The warm-up and a hover prefetch load screens while the redirect is on its way: they render, without the toast.
        foreach (var purpose in new[] { NavigationWarmUp.Value, NavigationWarmUp.Prefetch })
        {
            using var background = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
            background.Headers.Add(NavigationWarmUp.Header, purpose);
            var response = await client.SendAsync(background);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("dnf-sidebar", html);
            Assert.DoesNotContain(Saved, html);
        }

        // The screen the user opens next still shows it, once.
        Assert.Contains(Saved, await client.GetStringAsync("/admin/data"));
        Assert.DoesNotContain(Saved, await client.GetStringAsync("/admin/data"));
    }

    [Fact]
    public async Task Every_admin_screen_names_the_start_of_the_app()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var client = await factory.SignInAsync();

        var html = await client.GetStringAsync("/admin");
        Assert.Contains($"<body data-started=\"{NavigationWarmUp.StartId}\">", html);
    }
}

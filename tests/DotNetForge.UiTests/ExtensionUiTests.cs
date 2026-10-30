using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// An admin extension built on the library (.docs/ui/extensions.md): the bundled Audit Dashboard, opened in its admin
/// tab, loads dnf.css inside its frame, renders Dnf components with the host's tokens, follows the theme and is
/// scoped (<c>.dnf-scope</c>, <c>data-dnf-extension</c>).
/// </summary>
[Collection(UiCollection.Name)]
public sealed class ExtensionUiTests(UiHost host)
{
    private const string AuditDashboard = "dotnetforge.admin.audit-dashboard";

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Audit_dashboard_renders_dnf_components_in_its_frame(ColorScheme scheme)
    {
        var page = await host.NewPageAsync(scheme);

        // Enabled through the Plugins screen's own form (the browser holds the session and the antiforgery token).
        await page.GotoAsync("/admin/plugins");
        var enable = page.Locator($"form[action='/admin/plugins/{AuditDashboard}/enable']");
        if (await enable.CountAsync() > 0)
        {
            await enable.Locator("[type=submit]").ClickAsync();
            await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/admin/plugins");
        }

        var response = await page.GotoAsync($"/admin/ext/{AuditDashboard}");
        Assert.Equal(200, response!.Status);

        var frame = page.FrameLocator("iframe[data-frame]");
        var card = frame.Locator(".dnf-card").First;
        await Assertions.Expect(card).ToBeVisibleAsync();

        // Styled by dnf.css: the card's radius and border come from the tokens, not the browser defaults.
        Assert.Equal("10px", await card.EvaluateAsync<string>("c => getComputedStyle(c).borderTopLeftRadius"));
        Assert.NotEqual("0px", await card.EvaluateAsync<string>("c => getComputedStyle(c).borderTopWidth"));
        var scoped = await frame.Locator("[data-dnf-extension].dnf-scope").CountAsync();
        Assert.True(scoped > 0, "The extension root is not marked .dnf-scope[data-dnf-extension].");

        var surface = await card.EvaluateAsync<string>("c => getComputedStyle(c).backgroundColor");
        Assert.Equal(scheme == ColorScheme.Dark ? "rgb(22, 25, 30)" : "rgb(255, 255, 255)", surface);
        await page.Context.CloseAsync();
    }
}

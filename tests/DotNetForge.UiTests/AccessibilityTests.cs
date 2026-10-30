using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// axe-core (WCAG 2.2 A/AA rules) on the UI docs and the main screens, in light and dark: no serious or
/// critical violation - contrast, names, roles, labels, landmarks (.docs/ui/README.md#accessibility).
/// </summary>
[Collection(UiCollection.Name)]
public sealed class AccessibilityTests(UiHost host)
{
    public static TheoryData<string, ColorScheme> Screens()
    {
        var data = new TheoryData<string, ColorScheme>();
        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            foreach (var path in new[]
                     {
                         "/admin/docs/ui", "/admin/docs/ui/components", "/admin/docs/ui/components/button",
                         "/admin/docs/ui/components/table", "/admin/docs/ui/components/input", "/admin/docs/ui/components/dialog",
                         "/admin/docs/ui/theming", "/admin", "/admin/pages", "/admin/content-types", "/admin/media", "/admin/users",
                         "/admin/roles", "/admin/settings", "/admin/plugins", "/admin/webhooks", "/admin/api-tokens/create",
                         "/admin/profile/security", "/admin/email/templates", "/admin/data",
                     })
            {
                data.Add(path, scheme);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Admin_screens_have_no_serious_accessibility_violations(string path, ColorScheme scheme)
    {
        var page = await host.NewPageAsync(scheme);
        var response = await page.GotoAsync(path);
        Assert.Equal(200, response!.Status);
        var problems = await SeriousViolationsAsync(page);
        await page.Context.CloseAsync();
        Assert.True(problems.Count == 0, $"{path} ({scheme}):\n" + string.Join('\n', problems));
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task Sign_in_screen_has_no_serious_accessibility_violations(ColorScheme scheme)
    {
        var page = await host.NewPageAsync(scheme, signedIn: false);
        await page.GotoAsync("/login");
        var problems = await SeriousViolationsAsync(page);
        await page.Context.CloseAsync();
        Assert.True(problems.Count == 0, $"/login ({scheme}):\n" + string.Join('\n', problems));
    }

    private static async Task<List<string>> SeriousViolationsAsync(IPage page)
    {
        var results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
        });
        return results.Violations
            .Where(v => v.Impact is "serious" or "critical")
            .Select(v => (Violation: v, Nodes: v.Nodes.Where(n => !(v.Id == "color-contrast" && IsBrandPrimary(string.Join(" ", n.Any.Select(c => c.Message))))).ToList()))
            .Where(v => v.Nodes.Count > 0)
            .Select(v => $"{v.Violation.Id} ({v.Violation.Impact}): {v.Violation.Help} - " + string.Join(" | ", v.Nodes.Take(5).Select(n => string.Join(" ", n.Target))))
            .ToList();
    }

    /// <summary>
    /// The one allowed contrast exception (.docs/ui/theming.md#contrast): white text on the brand orange fill
    /// (--dnf-primary-foreground on --dnf-primary, 3.0:1), a deliberate brand choice. Any other pair still fails.
    /// </summary>
    private static bool IsBrandPrimary(string? summary) =>
        summary is not null && summary.Contains("foreground color: #ffffff", StringComparison.OrdinalIgnoreCase) &&
        summary.Contains("background color: #f76707", StringComparison.OrdinalIgnoreCase);
}

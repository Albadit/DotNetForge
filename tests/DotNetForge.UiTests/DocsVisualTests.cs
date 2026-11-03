using DotNetForge.Web.Areas.Admin.UiDocs;
using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// Visual regression of the component library through its docs (.docs/pages/ui-docs.md): the live preview of every
/// example on every page of the UI docs, in light and dark, compared with the recorded baselines (<see cref="Snapshots"/>);
/// and the table example stacked into cards on a phone.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class DocsVisualTests(UiHost host)
{
    public static TheoryData<string, ColorScheme> Pages()
    {
        var data = new TheoryData<string, ColorScheme>();
        foreach (var page in UiDocsCatalog.Pages)
        {
            data.Add(page.Href, ColorScheme.Light);
            data.Add(page.Href, ColorScheme.Dark);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Every_example_preview_matches_its_baseline(string path, ColorScheme scheme)
    {
        var page = await host.NewPageAsync(scheme, height: 2400);
        var response = await page.GotoAsync(path);
        Assert.Equal(200, response!.Status);
        await page.EvaluateAsync("document.fonts.ready");

        var slug = path[(path.LastIndexOf('/') + 1)..];
        var failures = new List<string>();
        foreach (var example in await page.Locator("[data-doc-example]").AllAsync())
        {
            var id = await example.GetAttributeAsync("data-doc-example");
            var preview = example.Locator("[data-doc-preview]").First;
            await preview.ScrollIntoViewIfNeededAsync();
            var png = await preview.ScreenshotAsync(new LocatorScreenshotOptions { Animations = ScreenshotAnimations.Disabled, Caret = ScreenshotCaret.Hide });
            if (Snapshots.Compare($"docs-{slug}-{id}-{scheme.ToString().ToLowerInvariant()}", png) is { } failure)
            {
                failures.Add(failure);
            }
        }

        await page.Context.CloseAsync();
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public async Task Tables_stack_into_cards_on_phones()
    {
        var page = await host.NewPageAsync(width: 390, height: 4000);
        await page.GotoAsync($"{UiDocsCatalog.Root}/components/table");
        var table = page.Locator("[data-doc-example='collection'] [data-doc-preview]");

        // Of the header row only the sortable columns stay, and each cell shows its label (td[data-label]::before).
        Assert.True(await table.Locator(".dnf-table__sort").First.IsVisibleAsync());
        Assert.False(await table.Locator("thead th:not(:has(.dnf-table__sort, [data-select-all]))").First.IsVisibleAsync());
        var label = await table.Locator("td[data-label='Status']").First.EvaluateAsync<string>("td => getComputedStyle(td, '::before').content");
        Assert.Equal("\"Status\"", label);

        await table.ScrollIntoViewIfNeededAsync();
        var png = await table.ScreenshotAsync(new LocatorScreenshotOptions { Animations = ScreenshotAnimations.Disabled });
        var failure = Snapshots.Compare("docs-table-collection-phone", png);
        await page.Context.CloseAsync();
        Assert.Null(failure);
    }
}

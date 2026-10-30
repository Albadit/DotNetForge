using DotNetForge.Web.Areas.Admin.UiDocs;
using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// The UI docs site itself (.docs/pages/ui-docs.md): every page renders with its examples, the example tabs show the
/// Razor and HTML of the preview without touching the URL, "On this page" follows the headings, and the search filters
/// the navigation.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class DocsTests(UiHost host)
{
    [Fact]
    public async Task Every_catalog_page_renders_its_title_and_examples()
    {
        var page = await host.NewPageAsync();
        var problems = new List<string>();
        foreach (var entry in UiDocsCatalog.Pages.Append(new UiDocsPage("components", "Components", "", "", false)))
        {
            var response = await page.GotoAsync(entry.Slug == "components" ? $"{UiDocsCatalog.Root}/components" : entry.Href);
            if (response!.Status != 200)
            {
                problems.Add($"{entry.Href}: {response.Status}");
                continue;
            }

            if (await page.Locator("h1").TextContentAsync() != entry.Title)
            {
                problems.Add($"{entry.Href}: h1 is not \"{entry.Title}\"");
            }

            if (entry.IsComponent && await page.Locator("[data-doc-example]").CountAsync() < 2)
            {
                problems.Add($"{entry.Href}: fewer than two examples");
            }

            if (entry.IsComponent && await page.Locator("h2#api").CountAsync() != 1)
            {
                problems.Add($"{entry.Href}: no API section");
            }
        }

        await page.Context.CloseAsync();
        Assert.True(problems.Count == 0, string.Join('\n', problems));
        Assert.True(host.ConsoleErrors.Count == 0, "Console errors:\n" + string.Join('\n', host.ConsoleErrors));
    }

    [Fact]
    public async Task Example_tabs_show_the_razor_and_html_of_the_preview_without_changing_the_url()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync($"{UiDocsCatalog.Root}/components/button");
        var example = page.Locator("[data-doc-example='variants']");

        await example.Locator("[data-tab='razor']").ClickAsync();
        await Assertions.Expect(example.Locator("[data-tab-panel='razor']")).ToBeVisibleAsync();
        await Assertions.Expect(example.Locator("[data-tab-panel='preview']")).ToBeHiddenAsync();
        await Assertions.Expect(example.Locator("[data-tab-panel='razor'] code")).ToContainTextAsync("<dnf-button variant=\"Primary\">Primary</dnf-button>");

        await example.Locator("[data-tab='html']").ClickAsync();
        await Assertions.Expect(example.Locator("[data-tab-panel='html'] code")).ToContainTextAsync("class=\"dnf-button dnf-button--primary\"");
        Assert.DoesNotContain("#", page.Url, StringComparison.Ordinal);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Before_dnf_js_runs_only_the_preview_of_an_example_shows()
    {
        // The moment between the first paint and the deferred dnf.js, frozen: dnf.js never arrives.
        var page = await host.NewPageAsync();
        await page.RouteAsync("**/dnf.js*", route => route.AbortAsync());
        await page.GotoAsync($"{UiDocsCatalog.Root}/components/button");

        var example = page.Locator("[data-doc-example='variants']");
        Assert.False(await page.EvaluateAsync<bool>("() => 'dnfUI' in window"));
        await Assertions.Expect(example.Locator("[data-tab-panel='preview']")).ToBeVisibleAsync();
        await Assertions.Expect(example.Locator("[data-tab-panel='razor']")).ToBeHiddenAsync();
        await Assertions.Expect(example.Locator("[data-tab-panel='html']")).ToBeHiddenAsync();
        Assert.Equal("true", await example.Locator("[data-tab='preview']").GetAttributeAsync("aria-selected"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task On_this_page_lists_the_headings_and_search_filters_the_navigation()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync($"{UiDocsCatalog.Root}/components/button");

        var toc = page.Locator("[data-docs-toc] a");
        Assert.True(await toc.CountAsync() > 5);
        Assert.Equal("#usage", await toc.First.GetAttributeAsync("href"));
        Assert.Equal("true", await toc.First.GetAttributeAsync("aria-current"));

        await page.Keyboard.PressAsync("/");
        await page.Keyboard.TypeAsync("dialo");
        var visible = page.Locator("[data-docs-nav] li:not([hidden]) a");
        await Assertions.Expect(visible).ToHaveCountAsync(1);
        await Assertions.Expect(visible).ToHaveTextAsync("Dialog");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(url => url.EndsWith("/components/dialog", StringComparison.Ordinal));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Copy_for_ai_puts_the_page_or_all_docs_on_the_clipboard_as_markdown()
    {
        var page = await host.NewPageAsync();
        await page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await page.GotoAsync($"{UiDocsCatalog.Root}/components/button");

        async Task<string> CopyAsync(string which)
        {
            await page.Locator(".dnf-docs__page-meta .dnf-dropdown > button").ClickAsync();
            await page.Locator($"[data-docs-copy='{which}']").ClickAsync();
            var toast = which == "all" ? "Copied all docs" : "Copied this page";
            await Assertions.Expect(page.Locator("[data-toasts] .dnf-toast", new() { HasText = toast })).ToBeVisibleAsync();
            // The system clipboard may turn \n into \r\n (Windows).
            return (await page.EvaluateAsync<string>("navigator.clipboard.readText()")).Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        var markdown = await CopyAsync("page");
        Assert.StartsWith("DotNetForge UI - the component library", markdown, StringComparison.Ordinal);
        Assert.Contains("# Button\n", markdown, StringComparison.Ordinal);
        Assert.Contains("## Usage", markdown, StringComparison.Ordinal);
        Assert.Contains("### Variants", markdown, StringComparison.Ordinal);
        Assert.Contains("```cshtml\n<dnf-button variant=\"Primary\">Primary</dnf-button>", markdown, StringComparison.Ordinal);
        Assert.Contains("| Attribute | Type | Default | Description |", markdown, StringComparison.Ordinal);
        Assert.Contains("| `variant` |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Preview", markdown, StringComparison.Ordinal);

        var all = await CopyAsync("all");
        foreach (var entry in UiDocsCatalog.Pages)
        {
            Assert.Contains($"# {entry.Title}\n", all, StringComparison.Ordinal);
        }

        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task The_account_menu_links_the_docs_for_super_admins()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin");
        await page.Locator(".dnf-user-menu__button").ClickAsync();
        await page.Locator("#account-menu a[href='/admin/docs/ui']").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/admin/docs/ui");
        Assert.Equal("Introduction", await page.Locator("h1").TextContentAsync());
        await page.Context.CloseAsync();
    }
}

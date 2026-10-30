using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// dnf.js in a real browser (.docs/ui/behaviours.md): dropdowns, dialogs, the confirmation, tabs, collections,
/// tooltips and the theme switch work, the stylesheet's three customization levels apply, and no screen logs a
/// console error (a CSP violation or a missing asset shows up there).
/// </summary>
[Collection(UiCollection.Name)]
public sealed class BehaviourTests(UiHost host)
{
    [Fact]
    public async Task Dropdown_opens_in_the_top_layer_next_to_its_button_and_moves_focus_with_arrows()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin/docs/ui/components/dropdown");
        var button = page.Locator("[data-doc-example='text-trigger'] .dnf-dropdown > button");
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var menu = page.Locator("[data-doc-example='text-trigger'] .dnf-dropdown__menu");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        Assert.Equal("true", await button.GetAttributeAsync("aria-expanded"));
        var buttonBox = (await button.BoundingBoxAsync())!;
        var menuBox = (await menu.BoundingBoxAsync())!;
        Assert.InRange(menuBox.Y - (buttonBox.Y + buttonBox.Height), 0, 12);

        // The toggle event (async) moves focus to the first item; only then do the arrow keys act on the menu.
        await Assertions.Expect(menu.Locator(".dnf-dropdown__item").First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(menu.Locator(".dnf-dropdown__item", new() { HasText = "Light" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(menu).ToBeHiddenAsync();
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Confirmation_dialog_resolves_and_toasts_announce()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin/docs/ui/components/dialog");
        await page.Locator("[data-docs-confirm]").First.ClickAsync();

        var dialog = page.Locator("#dnf-confirm-dialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("[data-confirm-ok]")).ToHaveTextAsync("Delete");
        await dialog.Locator("[data-confirm-ok]").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();

        var toast = page.Locator("[data-toasts] .dnf-toast").Last;
        await Assertions.Expect(toast).ToContainTextAsync("Confirmed");
        Assert.Equal("status", await toast.GetAttributeAsync("role"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Tabs_show_one_panel_and_follow_the_arrow_keys()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin/docs/ui/components/tabs");
        // The example's own tab group, inside the example frame (itself a tab group: Preview / Razor / HTML).
        var root = page.Locator("[data-doc-example='tabs'] [data-doc-preview] [data-tabs]");
        Assert.Equal("true", await root.GetAttributeAsync("data-tabbed"));
        await Assertions.Expect(root.Locator("[data-tab-panel='general']")).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator("[data-tab-panel='seo']")).ToBeHiddenAsync();

        await root.Locator("[data-tab='general']").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(root.Locator("[data-tab-panel='seo']")).ToBeVisibleAsync();
        Assert.Equal("true", await root.Locator("[data-tab='seo']").GetAttributeAsync("aria-selected"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Collection_filters_selects_and_pages()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin/docs/ui/components/table");
        var table = page.Locator("[data-doc-example='collection'] [data-collection]");

        // 12 rows, 10 per page: the pager shows; the count reads the total.
        await Assertions.Expect(table.Locator("[data-collection-count]")).ToHaveTextAsync("12 total");
        await Assertions.Expect(table.Locator(".dnf-pagination")).ToBeVisibleAsync();

        await table.Locator("[data-collection-search]").FillAsync("pricing");
        await Assertions.Expect(table.Locator("[data-collection-count]")).ToHaveTextAsync("1 of 12");

        var row = table.Locator("tbody tr[data-item]:not([hidden])").First;
        await row.Locator("[data-select-item]").CheckAsync();
        await Assertions.Expect(table.Locator("[data-bulk-bar]")).ToBeVisibleAsync();
        await Assertions.Expect(table.Locator("[data-bulk-count]")).ToHaveTextAsync("1 selected");
        Assert.Equal("true", await row.GetAttributeAsync("data-selected"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Chevrons_turn_up_while_their_menu_or_select_list_is_open()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin");

        // The account menu (a dropdown trigger): aria-expanded turns its chevron.
        var chevron = page.Locator(".dnf-user-menu__button .dnf-dropdown__chevron");
        Assert.Equal("none", await chevron.EvaluateAsync<string>("i => getComputedStyle(i).rotate"));
        await page.Locator(".dnf-user-menu__button").ClickAsync();
        await Assertions.Expect(page.Locator("#account-menu")).ToBeVisibleAsync();
        await Assertions.Expect(chevron).ToHaveCSSAsync("rotate", "180deg");
        await page.Keyboard.PressAsync("Escape");

        // A select drawn as a customizable select: the list is a menu-like card and its chevron turns while open.
        await page.GotoAsync("/admin/docs/ui/components/table");
        if (await page.EvaluateAsync<bool>("CSS.supports('appearance', 'base-select')"))
        {
            var select = page.Locator("[data-doc-example='collection'] select[data-collection-filter]");
            await select.ClickAsync();
            Assert.Equal("180deg", await select.EvaluateAsync<string>("s => getComputedStyle(s, '::picker-icon').rotate"));
            Assert.Equal("10px", await select.EvaluateAsync<string>("s => getComputedStyle(s, '::picker(select)').borderTopLeftRadius"));
            await page.Keyboard.PressAsync("Escape");
        }

        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Media_view_switch_lays_the_items_out_as_rows_and_remembers_it()
    {
        // The switch also carries data-js-only: every behaviour on an element must run, not just the first.
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin/media");
        await page.Locator("[data-folder-new]").ClickAsync();
        await page.Locator("#new-folder").FillAsync("View switch");
        await page.Locator("#new-folder-dialog [type=submit]").Last.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var items = page.Locator("#media-items");
        await page.Locator("[data-view-button='list']").ClickAsync();
        Assert.Equal("list", await items.GetAttributeAsync("data-view-mode"));
        Assert.Equal("true", await page.Locator("[data-view-button='list']").GetAttributeAsync("aria-pressed"));
        var columns = await items.EvaluateAsync<string>("e => getComputedStyle(e).gridTemplateColumns");
        Assert.DoesNotContain(" ", columns.Trim());

        await page.ReloadAsync();
        Assert.Equal("list", await page.Locator("#media-items").GetAttributeAsync("data-view-mode"));
        await page.Locator("[data-view-button='grid']").ClickAsync();
        Assert.Equal("grid", await page.Locator("#media-items").GetAttributeAsync("data-view-mode"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Theme_switch_and_subtree_themes_change_the_tokens()
    {
        var page = await host.NewPageAsync(ColorScheme.Light);
        await page.GotoAsync("/admin/docs/ui/theming");
        string Background(string selector) => $"getComputedStyle(document.querySelector(\"{selector}\")).backgroundColor";

        var light = await page.EvaluateAsync<string>(Background("[data-doc-example='subtree-themes'] [data-theme='light']"));
        var dark = await page.EvaluateAsync<string>(Background("[data-doc-example='subtree-themes'] [data-theme='dark']"));
        Assert.NotEqual(light, dark);

        // The brand scope re-colours its primary button; its hover shade is derived from the new leaf token.
        var brand = await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.dnf-docs-demo-brand .dnf-button--primary')).backgroundColor");
        var normal = await page.EvaluateAsync<string>("getComputedStyle(document.querySelector(\"[data-doc-example='subtree-themes'] [data-theme='light'] .dnf-button--primary\")).backgroundColor");
        Assert.NotEqual(normal, brand);

        await page.EvaluateAsync("document.querySelector('[data-theme-set=\"dark\"]').click()");
        Assert.Equal("dark", await page.EvaluateAsync<string>("document.documentElement.getAttribute('data-theme')"));
        await page.EvaluateAsync("document.querySelector('[data-theme-set=\"system\"]').click()");
        Assert.Null(await page.EvaluateAsync<string?>("document.documentElement.getAttribute('data-theme')"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Customization_levels_override_components_without_important()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync("/admin/docs/ui/customization");
        var section = page.Locator("[data-doc-example='levels'] [data-doc-preview]");

        // Level 2: a Tailwind utility in class beats the component's radius (utilities layer > components layer).
        var level2 = await section.Locator(".dnf-button.rounded-full").EvaluateAsync<string>("b => getComputedStyle(b).borderTopLeftRadius");
        Assert.NotEqual("6px", level2);

        // Level 3: unlayered CSS that sets a component token beats the component's own value.
        var level3 = await section.Locator(".dnf-docs-demo-buttons .dnf-button").EvaluateAsync<string>("b => getComputedStyle(b).height");
        Assert.Equal("44px", level3);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Screens_load_every_asset_without_console_errors()
    {
        host.ConsoleErrors.Clear();
        var page = await host.NewPageAsync();
        var failed = new List<string>();
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && new Uri(response.Url).Host is "127.0.0.1" or "localhost")
            {
                failed.Add($"{response.Status} {response.Url}");
            }
        };

        foreach (var path in new[] { "/admin", "/admin/docs/ui", "/admin/docs/ui/components/table", "/admin/pages", "/admin/content", "/admin/media", "/admin/settings", "/admin/plugins" })
        {
            await page.GotoAsync(path);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        await page.Context.CloseAsync();
        Assert.True(failed.Count == 0, "Failed requests:\n" + string.Join('\n', failed));
        Assert.True(host.ConsoleErrors.Count == 0, "Console errors:\n" + string.Join('\n', host.ConsoleErrors));
    }
}

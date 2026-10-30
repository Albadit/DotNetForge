using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// Screens don't flicker (.docs/ui/behaviours.md#before-the-first-paint). dnf.js is deferred, so on a real network the
/// page can paint before it runs. These tests hold dnf.js back and check that what it would change is already in place.
/// </summary>
public sealed partial class MobileTests
{
    /// <summary>The share of the screen that may move while a screen loads. Browsers call anything under 0.1 good.</summary>
    private const double MaxLayoutShift = 0.03;

    private static readonly TimeSpan ScriptDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Remembered choices, and every layout shift that wasn't caused by the user.</summary>
    private const string StabilityScript = """
        try {
            localStorage.setItem("dnf-view:media", "list");
            localStorage.setItem("dnf-page-size:/admin/users:0", "10");
        } catch (e) {}
        window.layoutShifts = [];
        new PerformanceObserver(list => {
            for (const entry of list.getEntries()) {
                if (!entry.hadRecentInput) {
                    window.layoutShifts.push({ value: entry.value, sources: (entry.sources || []).slice(0, 3).map(s => s.node ? (s.node.className || s.node.nodeName) : "?") });
                }
            }
        }).observe({ type: "layout-shift", buffered: true });
        """;

    private async Task<IPage> NewSlowPageAsync(string device, TimeSpan delay)
    {
        var (width, height) = device == "desktop" ? (1280, 900) : Devices[device];
        var page = await Host.NewPageAsync(width: width, height: height, touch: device != "desktop", motion: true);
        await page.AddInitScriptAsync(script: AuditScript);
        await page.AddInitScriptAsync(script: StabilityScript);
        await page.RouteAsync("**/dnf.js*", async route =>
        {
            await Task.Delay(delay);
            await route.ContinueAsync();
        });
        return page;
    }

    public static TheoryData<string> StabilityDevices() => new("desktop", "phone", "tablet");

    [Theory]
    [MemberData(nameof(StabilityDevices))]
    public async Task Screens_stay_still_while_they_load(string device)
    {
        var page = await NewSlowPageAsync(device, ScriptDelay);
        var failures = new List<string>();
        await CrawlAsync(page, async path =>
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(150);
            var shift = await page.EvaluateAsync<double>("() => window.layoutShifts.reduce((sum, s) => sum + s.value, 0)");
            if (shift > MaxLayoutShift)
            {
                failures.Add($"{device} {path}: moved {shift:0.000}, " + await page.EvaluateAsync<string>("() => JSON.stringify(window.layoutShifts.map(s => s.sources))"));
            }
        });

        await page.Context.CloseAsync();
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [Fact]
    public async Task What_was_saved_is_in_place_before_the_script_arrives()
    {
        var page = await NewSlowPageAsync("desktop", TimeSpan.FromSeconds(3));

        await page.GotoAsync("/admin/media", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => document.readyState !== 'loading'");
        Assert.False(await page.EvaluateAsync<bool>("() => 'dnfUI' in window")); // dnf.js hasn't run
        Assert.Equal("list", await page.Locator("#media-items").GetAttributeAsync("data-view-mode"));
        Assert.Equal("true", await page.Locator("[data-view-button=list]").GetAttributeAsync("aria-pressed"));
        Assert.Equal("false", await page.Locator("[data-view-button=grid]").GetAttributeAsync("aria-pressed"));
        await Assertions.Expect(page.Locator("[data-view-switch]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#media-search")).ToBeVisibleAsync();

        // The count the server wrote is still there once the script has run.
        var count = await page.Locator("[data-collection-count]").InnerTextAsync();
        await page.WaitForFunctionAsync("() => 'dnfUI' in window");
        await Assertions.Expect(page.Locator("[data-collection-count]")).ToHaveTextAsync(count);

        await page.GotoAsync("/admin/users", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => document.readyState !== 'loading'");
        Assert.False(await page.EvaluateAsync<bool>("() => 'dnfUI' in window"));
        await Assertions.Expect(page.Locator("tbody tr[data-item]:not([hidden])")).ToHaveCountAsync(10); // the saved page size
        await page.Context.CloseAsync();
    }

    /// <summary>Opens a content page in the page editor; the address it stays on, without a tab in it.</summary>
    private static async Task<string> OpenPageEditorAsync(IPage page)
    {
        await page.GotoAsync("/admin/pages");
        await page.Locator("[data-tree-item] a[href*='selected=']").First.ClickAsync();
        await page.Locator("[data-tabs][data-tabbed='true']").WaitForAsync();
        return new Uri(page.Url).PathAndQuery;
    }

    [Theory]
    [InlineData("seo")]
    [InlineData("theme")]
    [InlineData("layout")]
    public async Task A_refresh_stays_on_the_open_tab(string tab)
    {
        var page = await Host.NewPageAsync();
        await OpenPageEditorAsync(page);

        await page.Locator($"[data-tab='{tab}']").ClickAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator($"#panel-{tab}")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#panel-page")).ToBeHiddenAsync();
        Assert.Equal("true", await page.Locator($"[data-tab='{tab}']").GetAttributeAsync("aria-selected"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task The_open_tab_is_in_place_before_the_script_arrives()
    {
        var page = await NewSlowPageAsync("desktop", TimeSpan.FromSeconds(3));
        var editor = await OpenPageEditorAsync(page);

        async Task LoadAsync(string url)
        {
            await page.GotoAsync("about:blank"); // a new hash alone wouldn't load the screen again
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("() => document.readyState !== 'loading'");
            Assert.False(await page.EvaluateAsync<bool>("() => 'dnfUI' in window")); // dnf.js hasn't run
        }

        // A panel's title only shows without JavaScript: the tab names it.
        async Task AssertOpenAsync(string tab)
        {
            Assert.True(await page.Locator($"[data-tab-panel='{tab}']").IsVisibleAsync()); // now, not once the script arrives
            Assert.Equal(1, await page.Locator("[data-tab-panel]:visible").CountAsync());
            Assert.Equal("true", await page.Locator($"[data-tab='{tab}']").GetAttributeAsync("aria-selected"));
            Assert.Equal(1, await page.Locator("[role=tab][aria-selected='true']").CountAsync());
            Assert.True(await page.Locator($"[data-tab-panel='{tab}'] .dnf-tab-panel__title").EvaluateAsync<bool>("title => title.getBoundingClientRect().height <= 1"));
        }

        await LoadAsync(editor + "#seo");
        await AssertOpenAsync("seo");

        await LoadAsync(editor);
        await AssertOpenAsync("page");

        // Something in the address that isn't a tab.
        await LoadAsync(editor + "#nothing'\"]");
        await AssertOpenAsync("page");

        // Nothing moves when the script arrives.
        await page.WaitForFunctionAsync("() => 'dnfUI' in window");
        await AssertOpenAsync("page");
        Assert.InRange(await page.EvaluateAsync<double>("() => window.layoutShifts.reduce((sum, s) => sum + s.value, 0)"), 0, MaxLayoutShift);
        await page.Context.CloseAsync();
    }

    /// <summary>A fade between two screens shows both at once, on every click.</summary>
    [Fact]
    public async Task Changing_screens_is_a_clean_cut()
    {
        var page = await Host.NewPageAsync(motion: true);
        await page.AddInitScriptAsync(script: """
            window.addEventListener("pagereveal", async event => {
                if (!event.viewTransition) { return; }
                await event.viewTransition.ready;
                window.transitionAnimations = document.getAnimations()
                    .filter(a => a.effect.pseudoElement?.startsWith("::view-transition") && a.effect.getComputedTiming().duration > 0)
                    .map(a => a.effect.pseudoElement);
            });
            """);
        await page.GotoAsync("/admin/users");
        await page.Locator(".dnf-sidebar a[href='/admin/media']").ClickAsync();
        await page.WaitForURLAsync("**/admin/media");
        await page.WaitForFunctionAsync("() => 'transitionAnimations' in window");
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.transitionAnimations"));
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Switching_views_changes_one_thing_and_keeps_your_place()
    {
        var page = await NewDevicePageAsync("small-phone");
        await page.AddInitScriptAsync(script: """try { localStorage.setItem("dnf-view:media", "grid"); } catch (e) {}""");
        await page.GotoAsync("/admin/media");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // A card partly above the screen stays in sight, though a row is much shorter than a card.
        await page.EvaluateAsync("""
            () => {
                const cards = [...document.querySelectorAll("#media-items [data-item]")];
                window.scrollTo({ top: cards[2].getBoundingClientRect().top + scrollY - 120, behavior: "instant" });
                window.anchorCard = cards.find(card => card.getBoundingClientRect().bottom > 0);
                document.querySelector("[data-view-button=list]").click();
            }
            """);
        Assert.Equal("list", await page.Locator("#media-items").GetAttributeAsync("data-view-mode"));
        Assert.InRange(await page.EvaluateAsync<double>("() => window.anchorCard.getBoundingClientRect().top"), 0, Devices["small-phone"].Height);
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.getAnimations().filter(a => document.querySelector('#media-items').contains(a.effect?.target)).length"));

        // A card fully on the screen doesn't move at all.
        var before = await page.EvaluateAsync<double>("""
            () => {
                document.querySelector("[data-view-button=grid]").click();
                window.scrollTo({ top: 40, behavior: "instant" }); // room for the list's smaller padding
                window.anchorCard = document.querySelector("#media-items [data-item]");
                return window.anchorCard.getBoundingClientRect().top;
            }
            """);
        await page.EvaluateAsync("() => document.querySelector('[data-view-button=list]').click()");
        Assert.Equal(before, await page.EvaluateAsync<double>("() => window.anchorCard.getBoundingClientRect().top"), 1);
        await page.Context.CloseAsync();
    }
}

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

    /// <summary>
    /// A click needs its press and release on one page, so a screen arriving between the two swallows it. The sidebar
    /// goes on the press, here held down for good; the other ways of following a link stay as they are.
    /// </summary>
    [Fact]
    public async Task A_new_screen_cant_swallow_a_click_on_the_sidebar()
    {
        var page = await Host.NewPageAsync();
        await page.GotoAsync("/admin/users");
        var media = page.Locator(".dnf-sidebar a[href='/admin/media']").First;

        foreach (var press in new[] { "button: 2, pointerType: 'mouse'", "button: 0, pointerType: 'mouse', ctrlKey: true", "button: 0, pointerType: 'touch'" })
        {
            await media.EvaluateAsync($"link => link.dispatchEvent(new PointerEvent('pointerdown', {{ bubbles: true, {press} }}))");
        }

        await page.WaitForTimeoutAsync(200);
        Assert.Equal("/admin/users", new Uri(page.Url).AbsolutePath);

        var box = (await media.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 20, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.WaitForURLAsync("**/admin/media");
        await page.Mouse.UpAsync();

        await page.Locator(".dnf-sidebar a[href='/admin/users']").First.PressAsync("Enter");
        await page.WaitForURLAsync("**/admin/users");
        await page.Context.CloseAsync();
    }

    /// <summary>
    /// A click shows at once: the chosen link looks chosen and the one being left doesn't, and the loading bar is set
    /// going, still invisible, so a quick screen never shows it. Read in the same moment as the press, before the next
    /// screen can arrive.
    /// </summary>
    [Fact]
    public async Task A_click_shows_at_once_and_the_loading_bar_waits()
    {
        var page = await Host.NewPageAsync(motion: true);
        await page.GotoAsync("/admin/users");

        Assert.Equal("pressed | navigating | bar waits | left", await page.EvaluateAsync<string>("""
            () => {
                const media = document.querySelector(".dnf-sidebar a[href='/admin/media']");
                media.dispatchEvent(new PointerEvent("pointerdown", { bubbles: true, pointerType: "mouse", button: 0 }));
                const bar = getComputedStyle(document.querySelector(".dnf-shell"), "::after");
                const left = getComputedStyle(document.querySelector(".dnf-sidebar a[href='/admin/users']"), "::before");
                return [
                    media.hasAttribute("data-pressed") ? "pressed" : "not pressed",
                    document.documentElement.hasAttribute("data-navigating") ? "navigating" : "not navigating",
                    bar.content !== "none" && bar.transform === "matrix(0, 0, 0, 1, 0, 0)" ? "bar waits" : "bar: " + bar.content + " " + bar.transform,
                    left.content === "none" ? "left" : "still current",
                ].join(" | ");
            }
            """));

        await page.WaitForURLAsync("**/admin/media");
        Assert.Null(await page.Locator("html").GetAttributeAsync("data-navigating"));
        await page.Context.CloseAsync();
    }

    /// <summary>
    /// After a start, the first screen a browser shows loads the other sidebar screens in the background, once, so the
    /// first click on each isn't slow.
    /// </summary>
    [Fact]
    public async Task The_first_screen_after_a_start_warms_up_the_others()
    {
        var page = await Host.NewPageAsync();
        var warmed = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Headers.TryGetValue("x-dnf-purpose", out var purpose) && purpose == "warm-up")
            {
                lock (warmed)
                {
                    warmed.Add(new Uri(request.Url).AbsolutePath);
                }
            }
        };

        int WarmedCount()
        {
            lock (warmed)
            {
                return warmed.Distinct().Count();
            }
        }

        await page.GotoAsync("/admin/users");
        await page.EvaluateAsync("() => localStorage.removeItem('dnf-warm')"); // as if the app had just started
        await page.ReloadAsync();
        var expected = await page.EvaluateAsync<string[]>("""
            () => [...new Set([...document.querySelectorAll(".dnf-sidebar .dnf-nav a[href^='/admin']:not([aria-current])")]
                .map(a => a.getAttribute("href")).filter(h => !h.startsWith("/admin/ext/")))]
            """);
        Assert.NotEmpty(expected);
        for (var waited = 0; waited < 20000 && WarmedCount() < expected.Length; waited += 100)
        {
            await page.WaitForTimeoutAsync(100);
        }

        lock (warmed)
        {
            Assert.Equal(expected.OrderBy(h => h), warmed.Distinct().OrderBy(h => h));
        }

        // Once per start: the next screen doesn't do it again.
        var before = warmed.Count;
        await page.Locator(".dnf-sidebar a[href='/admin/roles']").First.ClickAsync();
        await page.WaitForURLAsync("**/admin/roles");
        await page.WaitForTimeoutAsync(1000);
        Assert.Equal(before, warmed.Count);
        await page.Context.CloseAsync();
    }

    /// <summary>The screen as the user sees it: address, title, heading, active link, and whether the page was kept.</summary>
    private const string ScreenState = """
        () => [location.pathname, document.title, document.querySelector("h1")?.textContent.trim(),
               document.querySelector(".dnf-sidebar .dnf-nav a[aria-current]")?.getAttribute("href"),
               window.kept === true ? "same page" : "new page"].join(" | ")
        """;

    /// <summary>
    /// Like a Nuxt layout: the sidebar stays and only the screen changes, and each screen looks exactly as a full load of
    /// its address does. Back and Forward go through the same screens.
    /// </summary>
    [Fact]
    public async Task The_sidebar_stays_and_only_the_screen_changes()
    {
        var page = await Host.NewPageAsync(motion: true);
        await page.GotoAsync("/admin/users");
        await page.EvaluateAsync("() => { window.kept = true; }");

        var seen = new List<(string Path, string State)>();
        foreach (var path in new[] { "/admin/roles", "/admin/media", "/admin/pages", "/admin/content" })
        {
            await page.Locator($".dnf-sidebar .dnf-nav a[href='{path}']").First.ClickAsync();
            await page.WaitForURLAsync("**" + path);
            seen.Add((path, await page.EvaluateAsync<string>(ScreenState)));
        }

        // What a full load of each address shows.
        var fresh = await Host.NewPageAsync(motion: true);
        foreach (var (path, state) in seen)
        {
            await fresh.GotoAsync(path);
            Assert.Equal((await fresh.EvaluateAsync<string>(ScreenState)).Replace("new page", "same page"), state);
        }

        await fresh.Context.CloseAsync();

        // Back and Forward: the address changes at once, the screen follows a moment later.
        async Task ArrivesAsync(string expected)
        {
            try
            {
                await page.WaitForFunctionAsync($"expected => ({ScreenState})() === expected", expected, new PageWaitForFunctionOptions { Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                Assert.Equal(expected, await page.EvaluateAsync<string>(ScreenState));
            }
        }

        await page.GoBackAsync();
        await ArrivesAsync(seen[2].State);
        await page.GoBackAsync();
        await ArrivesAsync(seen[1].State);
        await page.GoBackAsync();
        await ArrivesAsync(seen[0].State);
        await page.GoForwardAsync();
        await ArrivesAsync(seen[1].State);
        await page.Context.CloseAsync();
    }

    /// <summary>A screen's script runs again for each visit; what it bound to the document goes when the screen does.</summary>
    [Fact]
    public async Task Leaving_a_screen_removes_its_page_wide_listeners()
    {
        var page = await Host.NewPageAsync(motion: true);
        var cdp = await page.Context.NewCDPSessionAsync(page);
        async Task<int> DocumentListenersAsync(string type)
        {
            var document = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "document" });
            var id = document!.Value.GetProperty("result").GetProperty("objectId").GetString()!;
            var listeners = await cdp.SendAsync("DOMDebugger.getEventListeners", new Dictionary<string, object> { ["objectId"] = id });
            return listeners!.Value.GetProperty("listeners").EnumerateArray().Count(l => l.GetProperty("type").GetString() == type);
        }

        async Task GoAsync(string path)
        {
            await page.Locator($".dnf-sidebar .dnf-nav a[href='{path}']").First.ClickAsync();
            await page.WaitForURLAsync("**" + path);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(300); // the screen's scripts
        }

        await page.GotoAsync("/admin/users");
        await page.EvaluateAsync("() => { window.kept = true; }");
        var before = await DocumentListenersAsync("dragover");
        await GoAsync("/admin/media");
        var onMedia = await DocumentListenersAsync("dragover");
        Assert.True(onMedia > before, $"Media binds dragover to the document: {before} before, {onMedia} on Media");
        await GoAsync("/admin/users");
        Assert.Equal(before, await DocumentListenersAsync("dragover"));
        await GoAsync("/admin/media");
        Assert.Equal(onMedia, await DocumentListenersAsync("dragover"));

        // And the screen works: its own script opens the upload panel.
        await page.Locator("[data-upload-toggle]").ClickAsync();
        await Assertions.Expect(page.Locator("dialog[open]")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => window.kept === true"));
        await page.Context.CloseAsync();
    }

    /// <summary>
    /// Anything that doesn't fit is an ordinary page load: leaving a screen whose scripts haven't joined (the template
    /// editor's Monaco), and leaving one with unsaved work, where the browser then asks.
    /// </summary>
    [Fact]
    public async Task Screens_that_cant_be_swapped_load_as_before()
    {
        var page = await Host.NewPageAsync(motion: true);
        await page.GotoAsync("/admin/email/templates");
        await page.Locator("a.dnf-table__primary[href^='/admin/email/templates/']").First.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.EvaluateAsync("() => { window.kept = true; }");
        await page.Locator(".dnf-sidebar .dnf-nav a[href='/admin/media']").First.ClickAsync();
        await page.WaitForURLAsync("**/admin/media");
        await page.WaitForLoadStateAsync(LoadState.Load);
        Assert.EndsWith("new page", await page.EvaluateAsync<string>(ScreenState));

        string? asked = null;
        page.Dialog += async (_, dialog) =>
        {
            asked = dialog.Type;
            await dialog.DismissAsync();
        };
        await page.EvaluateAsync("() => { window.kept = true; window.dnfScreen.guard(() => true); window.addEventListener('beforeunload', e => { e.preventDefault(); e.returnValue = ''; }); }");
        await page.Locator("h1").ClickAsync(); // the browser only asks on a page that has been interacted with
        await page.Locator(".dnf-sidebar .dnf-nav a[href='/admin/pages']").First.ClickAsync();
        await page.WaitForTimeoutAsync(1000);
        Assert.Equal("beforeunload", asked);
        Assert.Equal("/admin/media", await page.EvaluateAsync<string>("() => location.pathname"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.kept === true"));
        await page.Context.CloseAsync();
    }

    /// <summary>Like NuxtLink: resting on a link loads its screen ahead - the screen only - and the click uses it.</summary>
    [Fact]
    public async Task Resting_on_a_link_loads_its_screen_ahead()
    {
        var page = await Host.NewPageAsync(motion: true);
        var loads = new List<string>();
        page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath == "/admin/roles")
            {
                lock (loads)
                {
                    var purpose = request.Headers.TryGetValue("x-dnf-purpose", out var p) ? p : "click";
                    loads.Add(purpose + (request.Headers.TryGetValue("x-dnf-screen", out var screen) && screen == "1" ? " (screen only)" : " (whole page)"));
                }
            }
        };
        await page.GotoAsync("/admin/users");
        var roles = page.Locator(".dnf-sidebar .dnf-nav a[href='/admin/roles']").First;
        await roles.HoverAsync();
        await page.WaitForTimeoutAsync(500);
        await roles.ClickAsync();
        await page.WaitForURLAsync("**/admin/roles");
        await page.WaitForTimeoutAsync(300);
        lock (loads)
        {
            Assert.Equal(new[] { "prefetch (screen only)" }, loads);
        }

        await page.Context.CloseAsync();
    }

    /// <summary>A screen seen before and unchanged comes back as 304, without its body, and looks exactly as it did.</summary>
    [Fact]
    public async Task A_screen_seen_before_comes_back_without_its_body()
    {
        var page = await Host.NewPageAsync(motion: true);
        var answers = new List<int>();
        page.Response += (_, response) =>
        {
            if (new Uri(response.Url).AbsolutePath == "/admin/users" && response.Request.Headers.ContainsKey("x-dnf-screen"))
            {
                lock (answers)
                {
                    answers.Add(response.Status);
                }
            }
        };

        await page.GotoAsync("/admin/roles");
        await page.EvaluateAsync("() => { window.kept = true; }");
        var users = page.Locator(".dnf-sidebar .dnf-nav a[href='/admin/users']").First;
        var roles = page.Locator(".dnf-sidebar .dnf-nav a[href='/admin/roles']").First;
        await users.ClickAsync();
        await page.WaitForURLAsync("**/admin/users");
        var first = await page.EvaluateAsync<string>(ScreenState);
        await roles.ClickAsync();
        await page.WaitForURLAsync("**/admin/roles");
        await users.ClickAsync();
        await page.WaitForURLAsync("**/admin/users");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(first, await page.EvaluateAsync<string>(ScreenState));
        lock (answers)
        {
            Assert.Equal(new[] { 200, 304 }, answers);
        }

        await page.Context.CloseAsync();
    }

    /// <summary>On a phone the sidebar is a drawer: choosing a screen closes it, as a page load did.</summary>
    [Fact]
    public async Task Choosing_a_screen_from_the_drawer_closes_it()
    {
        var page = await NewDevicePageAsync("phone");
        await page.GotoAsync("/admin/users");
        await page.Locator("[data-drawer-open]").TapAsync();
        Assert.Equal("true", await page.Locator("[data-shell]").GetAttributeAsync("data-nav-open"));
        await page.Locator(".dnf-sidebar .dnf-nav a[href='/admin/roles']").First.TapAsync();
        await page.WaitForURLAsync("**/admin/roles");
        Assert.NotEqual("true", await page.Locator("[data-shell]").GetAttributeAsync("data-nav-open"));
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
        // A full page load (the sidebar swaps screens in place, without one): a link in a screen, a reload, a fallback.
        await page.EvaluateAsync("() => location.assign('/admin/media')");
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

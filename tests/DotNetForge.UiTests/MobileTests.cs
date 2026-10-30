using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DotNetForge.Content;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Navigation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>A CMS of its own for the mobile tests: their rows never show up in the other browser tests.</summary>
public sealed class MobileHost : IAsyncLifetime
{
    public UiHost Ui { get; } = new();

    public async Task InitializeAsync()
    {
        await Ui.InitializeAsync();
        for (var i = 1; i <= 30; i++)
        {
            await Ui.Factory.CreateUserAsync($"person.with.a.long.address.{i}@example.com", "Sup3rSecret", "Editor");
        }

        using var scope = Ui.Factory.Services.CreateScope();
        var types = scope.ServiceProvider.GetRequiredService<ContentTypeService>();
        var entries = scope.ServiceProvider.GetRequiredService<ContentEntryService>();
        var (type, error) = await types.CreateAsync(null, "categories", null);
        Assert.True(type is not null, error);
        for (var i = 1; i <= 30; i++)
        {
            await entries.SaveAsync(type!, null, new JObject { ["name"] = $"A category with a rather long name {i}" }, true, null, null);
        }

        await Ui.Factory.WithDbAsync(async db =>
        {
            var factory = Ui.Factory.Services.GetRequiredService<DotNetForge.Abstractions.Security.IApiTokenFactory>();
            var generated = factory.Generate();
            db.ApiTokens.Add(new DotNetForge.Shared.Entities.ApiToken
            {
                Name = "Mobile test token",
                PermissionsCsv = PermissionKeys.ContentCreate,
                TokenHash = generated.Hash,
                TokenPrefix = generated.Prefix,
                ExpirationDate = DateTime.UtcNow.AddDays(30),
            });
            await db.SaveChangesAsync();
        });

        // Media through the real forms: a folder, then files with names too long for a phone.
        var page = await Ui.NewPageAsync();
        await page.GotoAsync("/admin/media");
        await page.Locator("[data-folder-new]").ClickAsync();
        await page.Locator("#new-folder").FillAsync("A folder with a rather long name");
        await page.Locator("#new-folder-dialog button[form=folder-create]").ClickAsync();
        await page.Locator(".dnf-media-card--folder").WaitForAsync();
        await page.Locator("[data-upload-toggle]").ClickAsync();
        await page.Locator("#files").SetInputFilesAsync(Enumerable.Range(1, 10).Select(i => new FilePayload
        {
            Name = $"a-file-with-a-rather-long-name-for-a-small-screen-{i}.txt",
            MimeType = "text/plain",
            Buffer = "Text"u8.ToArray(),
        }));
        await page.Locator("[data-upload-panel] button[type=submit]").ClickAsync();
        await page.Locator(".dnf-media-card:not(.dnf-media-card--folder)").First.WaitForAsync();

        await page.GotoAsync("/admin/media");
        await page.Locator("[data-upload-toggle]").ClickAsync();
        await page.Locator("#files").SetInputFilesAsync(new[] { "sample.docx", "sample.xlsx", "sample.pptx" }.Select(name => Fixture(name)));
        await page.Locator("[data-upload-panel] button[type=submit]").ClickAsync();
        await page.Locator("[data-preview-name='sample.pptx']").WaitForAsync();
        await page.Context.CloseAsync();
    }

    private static string Fixture(string name, [CallerFilePath] string file = "") =>
        Path.Combine(Path.GetDirectoryName(file)!, "..", "fixtures", "documents", name);

    public Task DisposeAsync() => Ui.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class MobileCollection : ICollectionFixture<MobileHost>
{
    public const string Name = "Mobile";
}

/// <summary>
/// The admin on touch devices (.docs/guides/testing.md#ui-tests): everything a desktop user can do has to work on a phone
/// and a tablet. <see cref="Every_screen_works"/> crawls the admin from the sidebar and checks each screen - with its
/// tabs, accordions, menus and dialogs opened - for overlapping or cut-off content, controls that can't be tapped, and
/// controls that exist on the desktop but not on the device. The other tests do real tasks with taps.
/// </summary>
[Collection(MobileCollection.Name)]
public sealed partial class MobileTests(MobileHost mobile)
{
    /// <summary>Above 64rem the admin shows its desktop layout, so the landscape tablet tests that one by touch.</summary>
    private static readonly Dictionary<string, (int Width, int Height)> Devices = new()
    {
        ["small-phone"] = (320, 568),
        ["phone"] = (390, 844),
        ["tablet"] = (820, 1180),
        ["tablet-landscape"] = (1180, 820),
    };

    /// <summary>A screen is opened for only this many examples of one route shape (every user is the same form).</summary>
    private const int SamplesPerShape = 2;

    /// <summary>Menus and dialogs opened per screen: every row of a table has the same menu.</summary>
    private const int OpenersPerScreen = 3;

    private static readonly string ResultsDirectory = Path.Combine(ProjectDir(), "TestResults", "mobile");

    private static string ProjectDir([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    private UiHost Host => mobile.Ui;

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")]
    private static partial Regex Id();

    public static TheoryData<string> DeviceNames() => new(Devices.Keys);

    /// <summary>The layouts that differ from the desktop one.</summary>
    public static TheoryData<string> SmallDevices() => new("small-phone", "phone", "tablet");

    private async Task<IPage> NewDevicePageAsync(string device)
    {
        var (width, height) = Devices[device];
        var page = await Host.NewPageAsync(width: width, height: height, touch: true);
        await page.AddInitScriptAsync(script: AuditScript);
        return page;
    }

    [Theory]
    [MemberData(nameof(DeviceNames))]
    public async Task Every_screen_works(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        var desktop = await Host.NewPageAsync();
        await desktop.AddInitScriptAsync(script: AuditScript);

        var failures = new List<string>();
        var screens = await CrawlAsync(page, async path =>
        {
            await page.EvaluateAsync("document.fonts.ready");
            var problems = new List<string>();
            var controls = new HashSet<string>();
            foreach (var state in await StatesAsync(page))
            {
                await page.EvaluateAsync("tab => window.mobileAudit.open(tab)", state);
                problems.AddRange(await page.EvaluateAsync<string[]>("([w, h]) => window.mobileAudit.problems(w, h)", new[] { width, height }));
                controls.UnionWith(await page.EvaluateAsync<string[]>("() => window.mobileAudit.keys()"));
            }

            problems.AddRange(await OpenersAsync(page, ".dnf-shell__main [popovertarget]", "popover", width, height));
            problems.AddRange(await OpenersAsync(page, ".dnf-shell__main [data-dialog-open]", "dialog", width, height));

            await desktop.GotoAsync(path);
            var missing = new HashSet<string>();
            foreach (var state in await StatesAsync(desktop))
            {
                await desktop.EvaluateAsync("tab => window.mobileAudit.open(tab)", state);
                missing.UnionWith(await desktop.EvaluateAsync<string[]>("() => window.mobileAudit.keys()"));
            }

            missing.ExceptWith(controls);
            problems.AddRange(missing.Select(key => $"only on the desktop: {key}"));

            if (problems.Count > 0)
            {
                failures.AddRange(problems.Distinct().Select(p => $"{device} {path}: {p}"));
                Directory.CreateDirectory(ResultsDirectory);
                await page.EvaluateAsync("window.scrollTo(0, 0)");
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = Path.Combine(ResultsDirectory, $"{device}{path.Replace('/', '-')}.png"),
                    FullPage = true,
                });
            }
        });

        await page.Context.CloseAsync();
        await desktop.Context.CloseAsync();
        Assert.True(screens > 25, $"Only {screens} screens were found.");
        Assert.True(failures.Count == 0, $"{failures.Count} problems on {device}:\n" + string.Join('\n', failures));
    }

    /// <summary>Opens every admin screen from the sidebar on, and each one's links up to <see cref="SamplesPerShape"/>.</summary>
    private static async Task<int> CrawlAsync(IPage page, Func<string, Task> visit)
    {
        var queue = new Queue<string>(AdminNavigation.Tree
            .SelectMany(s => s.Links.Concat(s.Groups.SelectMany(g => g.Links)))
            .Where(l => !l.Planned)
            .Select(l => l.Href));
        var seen = new HashSet<string>(queue);
        var shapes = new Dictionary<string, int>();
        while (queue.TryDequeue(out var path))
        {
            var response = await page.GotoAsync(path);
            if (response is not { Status: 200 } || !page.Url.Contains("/admin", StringComparison.Ordinal))
            {
                continue;
            }

            await visit(path);
            foreach (var link in await page.EvaluateAsync<string[]>("() => window.mobileAudit.links()"))
            {
                var shape = Id().Replace(link[..Math.Max(link.LastIndexOf('/'), 1)], "{id}");
                shapes.TryGetValue(shape, out var count);
                if (count < SamplesPerShape && !link.StartsWith("/admin/docs", StringComparison.Ordinal)
                    && !link.Contains("/builder", StringComparison.Ordinal) && seen.Add(link))
                {
                    shapes[shape] = count + 1;
                    queue.Enqueue(link);
                }
            }
        }

        return seen.Count;
    }

    /// <summary>The audit itself: each thing it looks for, planted on a clean screen, is reported.</summary>
    [Fact]
    public async Task The_audit_notices_what_it_looks_for()
    {
        var (width, height) = Devices["phone"];
        var page = await NewDevicePageAsync("phone");
        await page.GotoAsync("/admin/users");
        await page.EvaluateAsync("""
            () => {
                const cell = document.querySelector("tbody td[data-label='Status']");
                const text = document.createElement("span");
                text.textContent = "Planted text on top of the badge";
                Object.assign(text.style, { position: "absolute", right: "16px", fontSize: "12px" });
                cell.prepend(text);
                const wide = document.createElement("div");
                Object.assign(wide.style, { width: "900px", height: "4px" });
                document.querySelector(".dnf-content").append(wide);
            }
            """);
        var problems = await page.EvaluateAsync<string[]>("([w, h]) => window.mobileAudit.problems(w, h)", new[] { width, height });
        Assert.Contains(problems, p => p.Contains("the page scrolls sideways", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Planted text", StringComparison.Ordinal));

        await page.GotoAsync("/admin/users");
        await page.EvaluateAsync("""
            () => {
                const cover = document.createElement("div");
                cover.className = "planted-cover";
                Object.assign(cover.style, { position: "fixed", inset: "0", zIndex: "9999" });
                document.body.append(cover);
            }
            """);
        var covered = await page.EvaluateAsync<string[]>("([w, h]) => window.mobileAudit.problems(w, h)", new[] { width, height });
        await page.Context.CloseAsync();
        Assert.Contains(covered, p => p.Contains("covered by", StringComparison.Ordinal) && p.Contains("planted-cover", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task The_drawer_opens_reaches_every_link_and_closes(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin");
        var shell = page.Locator("[data-shell]");
        await page.Locator("[data-drawer-open]").TapAsync();
        await Assertions.Expect(shell).ToHaveAttributeAsync("data-nav-open", "true");

        var closed = page.Locator(".dnf-sidebar details[data-nav-group]:not([open]) > summary");
        while (await closed.CountAsync() > 0)
        {
            await closed.First.TapAsync();
        }

        AssertClean(await ProblemsWithinAsync(page, ".dnf-sidebar", width, height));
        await page.Locator(".dnf-user-menu__button").TapAsync();
        AssertClean(await OpenedAsync(page, "popover", width, height));
        await page.EvaluateAsync("() => window.mobileAudit.close()");

        await page.Locator("[data-drawer-backdrop]").TapAsync(new LocatorTapOptions { Position = new Position { X = width - 8, Y = height / 2f } });
        await Assertions.Expect(shell).Not.ToHaveAttributeAsync("data-nav-open", "true");

        await page.Locator("[data-drawer-open]").TapAsync();
        await page.Locator(".dnf-sidebar a[href='/admin/users']").TapAsync();
        await page.WaitForURLAsync("**/admin/users");
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task A_table_filters_sorts_and_pages(string device)
    {
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/users");
        var names = page.Locator("tbody tr[data-item]:not([hidden]) .dnf-table__primary");

        var sort = page.Locator("thead .dnf-table__sort", new PageLocatorOptions { HasText = "User" });
        await sort.TapAsync();
        var ascending = await names.First.InnerTextAsync();
        await sort.TapAsync();
        await Assertions.Expect(names.First).Not.ToHaveTextAsync(ascending);

        await page.Locator(".dnf-pagination__pages .dnf-button", new PageLocatorOptions { HasText = "2" }).TapAsync();
        await Assertions.Expect(page.Locator(".dnf-pagination__summary")).ToContainTextAsync("26-");

        await page.Locator("[data-collection-search]").TapAsync();
        await page.Keyboard.TypeAsync("address.17@");
        await Assertions.Expect(names).ToHaveCountAsync(1);
        await page.Locator("#user-status").SelectOptionAsync("disabled");
        await Assertions.Expect(names).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task A_user_is_created_found_and_deleted(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        var email = $"tapped.{device}@example.com";
        await page.GotoAsync("/admin/users");
        await page.Locator("a[href='/admin/users/create']").TapAsync();
        await page.Locator("#FirstName").FillAsync("Tapped");
        await page.Locator("#Email").FillAsync(email);
        await page.Locator("#Password").FillAsync("Sup3rSecret-Tapped-1");
        await page.Locator("input[name=RoleIds]").First.TapAsync();
        await page.Locator("form button[type=submit].dnf-button--primary").First.TapAsync();
        await page.WaitForURLAsync(url => !url.Contains("/create", StringComparison.Ordinal));

        await page.GotoAsync("/admin/users");
        await page.Locator("[data-collection-search]").FillAsync(email);
        var row = page.Locator("tbody tr[data-item]:not([hidden])");
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await row.Locator("[popovertarget]").TapAsync();
        AssertClean(await OpenedAsync(page, "popover", width, height));
        await page.Locator("[popover]:popover-open .dnf-dropdown__item", new PageLocatorOptions { HasText = "Delete" }).TapAsync();
        await ConfirmAsync(page, width, height);

        await page.Locator("[data-collection-search]").FillAsync(email);
        await Assertions.Expect(row).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task Entries_are_selected_deleted_and_reordered(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/content/categories");
        var titles = page.Locator("tbody tr[data-entry-id] .dnf-table__primary");
        var bar = page.Locator("[data-bulk-bar]");

        // The bar takes the toolbar's place, and all of it can be reached.
        await page.Locator("tbody [data-select-item]").First.TapAsync();
        await Assertions.Expect(bar).ToBeVisibleAsync();
        AssertClean(await ProblemsWithinAsync(page, "[data-bulk-bar]", width, height));
        await bar.Locator("[data-select-all]").TapAsync();
        await Assertions.Expect(bar.Locator("[data-bulk-count]")).ToContainTextAsync("25");
        await bar.Locator("[data-bulk-clear]").TapAsync();
        await Assertions.Expect(bar).ToBeHiddenAsync();

        var deleted = await titles.First.InnerTextAsync();
        await page.Locator("tbody [data-select-item]").First.TapAsync();
        await bar.Locator("button[type=submit]").TapAsync();
        await ConfirmAsync(page, width, height);
        await Assertions.Expect(titles.First).Not.ToHaveTextAsync(deleted);

        // Reordering: dragging needs a mouse, the row's menu doesn't.
        var (first, second) = (await titles.Nth(0).InnerTextAsync(), await titles.Nth(1).InnerTextAsync());
        await page.Locator("tbody tr[data-entry-id]").First.Locator("[popovertarget]").TapAsync();
        await page.Locator("[popover]:popover-open .dnf-dropdown__item", new PageLocatorOptions { HasText = "Move down" }).TapAsync();
        await Assertions.Expect(titles.First).ToHaveTextAsync(second);
        await Assertions.Expect(titles.Nth(1)).ToHaveTextAsync(first);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task Pages_are_duplicated_and_reordered_from_the_row_menu(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/pages");
        var rows = page.Locator(".dnf-tree__row");
        var titles = page.Locator(".dnf-tree__title");
        var count = await rows.CountAsync();

        await rows.First.Locator("[popovertarget]").TapAsync();
        AssertClean(await OpenedAsync(page, "popover", width, height));
        await ReloadsAsync(page, () =>
            page.Locator("[popover]:popover-open .dnf-dropdown__item", new PageLocatorOptions { HasText = "Duplicate" }).TapAsync());
        await page.GotoAsync("/admin/pages");
        await Assertions.Expect(rows).ToHaveCountAsync(count + 1);

        // The second row: the home page in the first stays where it is.
        var second = page.Locator(".dnf-tree__item").Nth(1);
        var id = await second.GetAttributeAsync("data-id");
        await rows.Nth(1).Locator("[popovertarget]").TapAsync();
        await page.Locator("[popover]:popover-open .dnf-dropdown__item", new PageLocatorOptions { HasText = "Move down" }).TapAsync();
        await Assertions.Expect(second).Not.ToHaveAttributeAsync("data-id", id!);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task A_page_moves_under_another_and_back_to_root(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/pages");
        var roots = page.Locator(".dnf-content-manager__tree .dnf-card__body > .dnf-tree > .dnf-tree__item");
        var nested = page.Locator(".dnf-tree .dnf-tree .dnf-tree__title");
        var title = await roots.Last.Locator(".dnf-tree__title").First.InnerTextAsync();
        var parent = await roots.Nth(1).Locator(".dnf-tree__title").First.InnerTextAsync();

        async Task<ILocator> OpenAsync(ILocator row)
        {
            await row.Locator("[popovertarget]").First.TapAsync();
            await page.Locator("[popover]:popover-open .dnf-dropdown__item", new PageLocatorOptions { HasText = "Move…" }).TapAsync();
            var dialog = page.Locator("#page-move-dialog");
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            AssertClean(await OpenedAsync(page, "dialog", width, height));
            return dialog;
        }

        // A page can't go inside itself, and the dialog says what it is moving.
        var dialog = await OpenAsync(roots.Last);
        await Assertions.Expect(dialog.Locator("[data-move-subject]")).ToHaveTextAsync(title);
        var self = dialog.Locator(".dnf-move-option", new LocatorLocatorOptions { Has = page.Locator(".dnf-move-option__label", new PageLocatorOptions { HasTextString = title }) }).First;
        await Assertions.Expect(self).ToContainTextAsync("Can't go inside itself");
        await Assertions.Expect(dialog.Locator(".dnf-move-option__hint", new LocatorLocatorOptions { HasTextRegex = new Regex("^/$") })).ToHaveCountAsync(0); // never under Home

        await dialog.Locator(".dnf-move-option", new LocatorLocatorOptions { HasText = parent }).First.TapAsync();
        await ReloadsAsync(page, () => dialog.Locator("[data-move-submit]").TapAsync());
        await Assertions.Expect(nested.Filter(new LocatorFilterOptions { HasText = title })).ToHaveCountAsync(1);

        // And back: Root is the first choice.
        dialog = await OpenAsync(page.Locator(".dnf-tree .dnf-tree .dnf-tree__row", new PageLocatorOptions { HasText = title }).First);
        await dialog.Locator(".dnf-move-option").First.TapAsync();
        await ReloadsAsync(page, () => dialog.Locator("[data-move-submit]").TapAsync());
        await Assertions.Expect(nested.Filter(new LocatorFilterOptions { HasText = title })).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task A_file_moves_from_its_own_menu(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/media");
        var files = page.Locator(".dnf-media-card[data-preview-name^='a-file-with']");
        var count = await files.CountAsync();

        await files.First.Locator("[popovertarget]").TapAsync();
        await page.Locator("[popover]:popover-open .dnf-dropdown__item", new PageLocatorOptions { HasText = "Move…" }).TapAsync();
        var move = page.Locator("#media-move-dialog");
        await Assertions.Expect(move).ToBeVisibleAsync();
        AssertClean(await OpenedAsync(page, "dialog", width, height));
        await move.Locator(".dnf-move-option", new LocatorLocatorOptions { HasText = "A folder" }).TapAsync();
        await ReloadsAsync(page, () => move.Locator("[data-move-submit]").TapAsync());
        await Assertions.Expect(files).ToHaveCountAsync(count - 1);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task The_home_page_is_fixed_in_the_tree_and_the_form()
    {
        var page = await Host.NewPageAsync();
        await page.GotoAsync("/admin/pages");
        var home = page.Locator("[data-tree-home='true']");
        Assert.Equal("false", await home.GetAttributeAsync("draggable"));

        await home.Locator("[popovertarget]").First.ClickAsync(new LocatorClickOptions { Force = true });
        var menu = page.Locator("[popover]:popover-open");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        foreach (var item in new[] { "Move…", "Move up", "Move down", "Add child page", "Delete" })
        {
            await Assertions.Expect(menu.Locator(".dnf-dropdown__item", new LocatorLocatorOptions { HasText = item })).ToHaveCountAsync(0);
        }

        await page.GotoAsync("/admin/pages?selected=" + await home.GetAttributeAsync("data-id"));
        await Assertions.Expect(page.Locator("#Slug")).Not.ToBeEditableAsync();
        await Assertions.Expect(page.Locator("#SortOrder")).Not.ToBeEditableAsync();
        await Assertions.Expect(page.Locator("#ParentPageId")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#danger-title")).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    [Fact]
    public async Task Dragging_shows_where_a_page_lands_and_takes_its_children_along()
    {
        var page = await Host.NewPageAsync();
        await page.GotoAsync("/admin/pages");
        ILocator Item(string title) => page.Locator($".dnf-tree__item:has(> .dnf-tree__row .dnf-tree__title:text-is('{title}'))");
        ILocator Row(string title) => Item(title).Locator("> .dnf-tree__row");
        async Task HoverAsync(ILocator row, float share)
        {
            var box = (await row.BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height * share, new MouseMoveOptions { Steps = 4 });

            // Entering a row is only dragenter; the dragover that follows needs one more move (a real drag repeats it).
            await page.Mouse.MoveAsync(box.X + box.Width / 2 + 1, box.Y + box.Height * share);
        }

        var source = (await Row("About").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(source.X + source.Width / 3, source.Y + source.Height / 2);
        await page.Mouse.DownAsync();
        await HoverAsync(Row("News"), 0.5f);

        // The edges of a row are the gaps beside it, its middle is the page itself.
        await HoverAsync(Row("News"), 0.1f);
        await Assertions.Expect(Item("News")).ToHaveAttributeAsync("data-drop", "before");
        await HoverAsync(Row("News"), 0.9f);
        await Assertions.Expect(Item("News")).ToHaveAttributeAsync("data-drop", "after");

        // Beside Home only below it, and never inside itself.
        await HoverAsync(page.Locator("[data-tree-home='true'] > .dnf-tree__row"), 0.1f);
        await Assertions.Expect(page.Locator("[data-tree-home='true']")).ToHaveAttributeAsync("data-drop", "after");
        await HoverAsync(Row("Team"), 0.5f);
        await Assertions.Expect(page.Locator(".dnf-tree__item[data-drop]")).ToHaveCountAsync(0);

        await HoverAsync(Row("News"), 0.5f);
        await Assertions.Expect(Item("News")).ToHaveAttributeAsync("data-drop", "inside");
        await ReloadsAsync(page, () => page.Mouse.UpAsync());

        // About went inside News, and Team and History with it.
        await Assertions.Expect(Item("News").Locator(".dnf-tree__title", new LocatorLocatorOptions { HasText = "About" })).ToHaveCountAsync(1);
        await Assertions.Expect(Item("About").Locator(".dnf-tree__title", new LocatorLocatorOptions { HasText = "Team" })).ToHaveCountAsync(1);
        await Assertions.Expect(Item("About").Locator(".dnf-tree__title", new LocatorLocatorOptions { HasText = "History" })).ToHaveCountAsync(1);
        await page.Context.CloseAsync();

        // Other tests open /about.
        await Host.Factory.WithDbAsync(async db =>
        {
            (await db.Pages.SingleAsync(p => p.Slug == "about")).ParentPageId = null;
            await db.SaveChangesAsync();
        });
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task Media_is_uploaded_selected_and_moved(string device)
    {
        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/media");

        await page.Locator("[data-upload-toggle]").TapAsync();
        AssertClean(await OpenedAsync(page, "dialog", width, height));
        await page.Locator("[data-upload-close]").TapAsync();

        var files = page.Locator(".dnf-media-card[data-preview-name^='a-file-with']");
        AssertClean(await ProblemsWithinAsync(page, "#media-items", width, height));
        await files.First.Locator("[popovertarget]").TapAsync();
        AssertClean(await OpenedAsync(page, "popover", width, height));
        await page.EvaluateAsync("() => window.mobileAudit.close()");

        await page.Locator("[data-view-button=list]").TapAsync();
        AssertClean(await ProblemsWithinAsync(page, "#media-items", width, height));

        var count = await files.CountAsync();
        await files.First.Locator("[data-select-item]").TapAsync();
        var bar = page.Locator("[data-bulk-bar]");
        await Assertions.Expect(bar).ToBeVisibleAsync();
        AssertClean(await ProblemsWithinAsync(page, "[data-bulk-bar]", width, height));

        // Move… asks where to: Root is first and is where the file already is, so nothing can be sent yet.
        await bar.Locator("[data-media-move]").TapAsync();
        var move = page.Locator("#media-move-dialog");
        await Assertions.Expect(move).ToBeVisibleAsync();
        AssertClean(await OpenedAsync(page, "dialog", width, height));
        var rootOption = move.Locator(".dnf-move-option").First;
        await Assertions.Expect(rootOption).ToContainTextAsync("Root");
        await Assertions.Expect(rootOption).ToContainTextAsync("Already here");
        await Assertions.Expect(rootOption.Locator("input")).ToBeDisabledAsync();
        await Assertions.Expect(move.Locator("[data-move-submit]")).ToBeDisabledAsync();

        await move.Locator(".dnf-move-option", new LocatorLocatorOptions { HasText = "A folder" }).TapAsync();
        await ReloadsAsync(page, () => move.Locator("[data-move-submit]").TapAsync());
        await Assertions.Expect(files).ToHaveCountAsync(count - 1);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(SmallDevices))]
    public async Task Content_type_fields_are_reordered_without_dragging(string device)
    {
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/content-types");
        await page.Locator("a[href^='/admin/content-types/']:not([href$='/new'])").First.TapAsync();
        await page.WaitForURLAsync(url => Id().IsMatch(url));
        var fields = page.Locator(".dnf-field-builder__summary");
        var first = await fields.First.TextContentAsync();
        await page.Locator("button[aria-label='Move down']:not(:disabled)").First.TapAsync();
        await Assertions.Expect(fields.Nth(1)).ToHaveTextAsync(first!);
        await page.Context.CloseAsync();
    }

    [Theory]
    [MemberData(nameof(DeviceNames))]
    public async Task The_public_screens_work(string device)
    {
        var (width, height) = Devices[device];
        var page = await Host.NewPageAsync(signedIn: false, width: width, height: height, touch: true);
        await page.AddInitScriptAsync(script: AuditScript);
        var failures = new List<string>();
        foreach (var path in new[] { "/login", "/", "/about", "/error" })
        {
            await page.GotoAsync(path);
            failures.AddRange((await page.EvaluateAsync<string[]>("([w, h]) => window.mobileAudit.problems(w, h)", new[] { width, height }))
                .Select(p => $"{path}: {p}"));
        }

        await page.Context.CloseAsync();
        AssertClean(failures.ToArray());
    }

    [Theory]
    [MemberData(nameof(DeviceNames))]
    public async Task The_page_builder_works(string device)
    {
        var (width, height) = Devices[device];
        Guid id = default;
        await Host.Factory.WithDbAsync(async db => id = await db.Pages.Select(p => p.Id).FirstAsync());
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync($"/admin/pages/{id}/builder");
        await page.Locator(".pe-host").WaitForAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // A page without a layout greets with the starting points.
        var welcome = page.Locator(".pe-modal-backdrop");
        if (await welcome.IsVisibleAsync())
        {
            AssertClean(await ProblemsWithinAsync(page, ".pe-modal-backdrop", width, height));
            await welcome.Locator("button").First.TapAsync();
            await Assertions.Expect(welcome).ToBeHiddenAsync();
        }

        // The panels stay reachable, and on a phone the canvas gets the whole width.
        var problems = await ProblemsWithinAsync(page, ".pe-host", width, height);
        if (width < 768)
        {
            Assert.Equal(width, (await page.Locator(".pe-center").BoundingBoxAsync())!.Width, 1);
        }

        await page.Locator(".pe-left button", new PageLocatorOptions { HasText = "Insert" }).TapAsync();
        await Assertions.Expect(page.Locator(".pe-insert-grid").First).ToBeVisibleAsync();
        await page.Context.CloseAsync();
        AssertClean(problems);
    }

    /// <summary>Taps the confirmation dialog's OK after checking the dialog fits the screen.</summary>
    private static async Task ConfirmAsync(IPage page, int width, int height)
    {
        var dialog = page.Locator("#dnf-confirm-dialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        AssertClean(await OpenedAsync(page, "dialog", width, height));
        await ReloadsAsync(page, () => dialog.Locator("[data-confirm-ok]").TapAsync());
    }

    /// <summary>Does <paramref name="action"/> and waits for the screen it posts back to.</summary>
    private static async Task ReloadsAsync(IPage page, Func<Task> action)
    {
        await page.RunAndWaitForResponseAsync(action, response => response.Request.IsNavigationRequest && response.Request.Method == "GET");
        await page.WaitForLoadStateAsync();
    }

    private static void AssertClean(string[] problems) => Assert.True(problems.Length == 0, string.Join('\n', problems));

    private static Task<string[]> ProblemsWithinAsync(IPage page, string selector, int width, int height) =>
        page.EvaluateAsync<string[]>("([s, w, h]) => window.mobileAudit.within(s, w, h)", new object[] { selector, width, height });

    private static Task<string[]> OpenedAsync(IPage page, string kind, int width, int height) =>
        page.EvaluateAsync<string[]>("([k, w, h]) => window.mobileAudit.opened(k, w, h)", new object[] { kind, width, height });

    /// <summary>The tabs of the screen, each one a state to check; a screen without tabs has one state, itself.</summary>
    private static async Task<string?[]> StatesAsync(IPage page)
    {
        var tabs = await page.EvaluateAsync<string[]>("() => window.mobileAudit.tabs()");
        return tabs.Length == 0 ? new string?[] { null } : tabs;
    }

    /// <summary>Opens the first few menus or dialogs with a tap and checks what opened.</summary>
    private static async Task<IEnumerable<string>> OpenersAsync(IPage page, string selector, string kind, int width, int height)
    {
        var problems = new List<string>();
        var count = await page.Locator(selector).CountAsync();
        var opened = 0;
        for (var i = 0; i < count && opened < OpenersPerScreen; i++)
        {
            var opener = page.Locator(selector).Nth(i);
            if (!await opener.IsVisibleAsync())
            {
                continue;
            }

            opened++;
            var name = await opener.EvaluateAsync<string>("el => window.mobileAudit.describe(el)");
            try
            {
                await opener.TapAsync(new LocatorTapOptions { Timeout = 3000 });
            }
            catch (TimeoutException)
            {
                problems.Add($"can't tap {name}");
                continue;
            }

            problems.AddRange((await page.EvaluateAsync<string[]>(
                "([kind, w, h]) => window.mobileAudit.opened(kind, w, h)", new object[] { kind, width, height }))
                .Select(p => $"in {kind} of {name}: {p}"));
            await page.EvaluateAsync("() => window.mobileAudit.close()");
        }

        return problems;
    }

    /// <summary>Installed on every page; the page's CSP doesn't apply to it.</summary>
    private const string AuditScript = """
        window.mobileAudit = (() => {
            const selector = "a[href], button, input:not([type=hidden]), select, textarea, summary, [role=button], [contenteditable=true]";
            const main = () => document.querySelector(".dnf-shell__main") ?? document.body;

            const shown = el => {
                if (!el.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })) return false;
                const box = el.getBoundingClientRect();
                return box.width > 1 && box.height > 1;
            };
            const controls = scope => [...scope.querySelectorAll(selector)].filter(el =>
                shown(el) && !el.closest("[data-toasts], template, [aria-hidden=true], .monaco-editor") && el.tabIndex !== -1
                && !el.matches("[data-drawer-open], .dnf-skip-link") && !el.closest(".dnf-popover, [popover]:not(:popover-open)"));

            const text = el => (el.getAttribute("aria-label") || el.labels?.[0]?.innerText || el.innerText || el.title || el.placeholder || "").trim().replace(/\s+/g, " ").slice(0, 50);
            const describe = el => `<${el.tagName.toLowerCase()}${el.type ? " " + el.type : ""}> "${text(el) || el.getAttribute("href") || el.name || el.className}"`;
            const key = el => `${el.tagName.toLowerCase()} ${el.getAttribute("href")?.replace(/\?.*/, "") || el.name || text(el)}`;

            const scroller = el => {
                for (let parent = el.parentElement; parent; parent = parent.parentElement) {
                    const overflow = getComputedStyle(parent).overflowX;
                    if ((overflow === "auto" || overflow === "scroll") && parent.scrollWidth > parent.clientWidth + 1) return parent;
                }
                return null;
            };
            const clip = (el, box) => {
                const result = { left: box.left, top: box.top, right: box.right, bottom: box.bottom };
                for (let parent = el; parent; parent = parent.parentElement) {
                    const style = getComputedStyle(parent);
                    if (style.overflowX === "visible" && style.overflowY === "visible") continue;
                    const edge = parent.getBoundingClientRect();
                    result.left = Math.max(result.left, edge.left); result.top = Math.max(result.top, edge.top);
                    result.right = Math.min(result.right, edge.right); result.bottom = Math.min(result.bottom, edge.bottom);
                }
                return result;
            };
            const overlap = (a, b) => {
                const w = Math.min(a.right, b.right) - Math.max(a.left, b.left), h = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
                return w > 3 && h > 0.3 * Math.min(a.bottom - a.top, b.bottom - b.top) && h > 3;
            };

            /* Each control: on screen, not covered at its middle or its ends, and big enough for a finger. */
            const tappable = (scope, width, height, problems) => {
                for (const el of controls(scope)) {
                    if (el.matches(":disabled, [aria-disabled=true]")) continue;
                    el.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" });
                    const box = el.getClientRects()[0] ?? el.getBoundingClientRect();
                    if (!scroller(el) && (box.left < -1 || box.right > width + 1)) problems.push(`runs off the screen: ${describe(el)}`);
                    const y = Math.min(box.top + box.height / 2, height - 1);
                    for (const x of [box.left + box.width / 2, box.left + 3, box.right - 3]) {
                        if (x < 0 || x >= width || y < 0) continue;
                        const hit = document.elementFromPoint(x, y);
                        if (!hit || hit === el || el.contains(hit) || hit.closest("label")?.contains(el) || [...(el.labels ?? [])].some(l => l.contains(hit))) continue;
                        problems.push(`${hit.contains(el) ? "cut off by" : "covered by"} <${hit.tagName.toLowerCase()} class="${hit.className}">: ${describe(el)}`);
                        break;
                    }

                    const target = el.closest("label") ?? el;
                    const size = target.getBoundingClientRect();
                    const textLink = el.matches("a:not(.dnf-button, .dnf-segmented__item)");
                    if (!textLink && (size.height < 24 || size.width < 24)) problems.push(`too small to tap (${Math.round(size.width)}x${Math.round(size.height)}): ${describe(el)}`);
                }
            };

            /* Text on text, text on a control, and text the box cuts off. */
            const legible = (scope, problems) => {
                window.scrollTo(0, 0);
                document.querySelectorAll("*").forEach(el => { if (el.scrollTop > 0) el.scrollTop = 0; });
                const pieces = [];
                const walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT);
                for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                    const el = node.parentElement;
                    if (!node.textContent.trim() || !shown(el) || el.closest("[data-toasts], template, script, style, .sr-only, select, option, .monaco-editor")) continue;
                    const range = document.createRange();
                    range.selectNodeContents(node);
                    for (const line of range.getClientRects()) {
                        const box = clip(el, line);
                        if (box.right - box.left > 1 && box.bottom - box.top > 1) pieces.push({ el, box, text: node.textContent.trim().slice(0, 40) });
                    }
                }

                const fields = controls(scope).filter(el => el.matches("input, select, textarea, button, .dnf-button"))
                    .map(el => ({ el, box: clip(el, el.getBoundingClientRect()) }));
                for (let i = 0; i < pieces.length; i++) {
                    for (let j = i + 1; j < pieces.length; j++) {
                        if (pieces[i].el !== pieces[j].el && overlap(pieces[i].box, pieces[j].box)) problems.push(`text overlaps text: "${pieces[i].text}" / "${pieces[j].text}"`);
                    }
                    for (const field of fields) {
                        if (!field.el.contains(pieces[i].el) && overlap(pieces[i].box, field.box) && !pieces[i].el.closest("label")?.contains(field.el)) problems.push(`text overlaps ${describe(field.el)}: "${pieces[i].text}"`);
                    }
                }

                for (const el of scope.querySelectorAll("*")) {
                    if (!shown(el) || el.closest(".sr-only, .monaco-editor, .dnf-auth__panel") || !el.textContent.trim()) continue;
                    const style = getComputedStyle(el);
                    if ((style.overflowX === "hidden" || style.overflowX === "clip") && style.textOverflow !== "ellipsis" && el.scrollWidth > el.clientWidth + 1)
                        problems.push(`cuts off its content: <${el.tagName.toLowerCase()} class="${el.className}">`);
                }
            };

            return {
                describe,
                tabs: () => [...main().querySelectorAll("[role=tab][data-tab]")].filter(shown).map(t => t.getAttribute("data-tab")),
                open: tab => {
                    main().querySelectorAll("details").forEach(d => { d.open = true; });
                    if (tab) main().querySelector(`[role=tab][data-tab='${tab}']`)?.click();
                },
                keys: () => [...new Set(controls(main()).filter(el => !el.closest(".dnf-breadcrumbs")).map(key))],
                links: () => [...new Set([...document.querySelectorAll("a[href^='/admin']:not([download]):not([target])")].map(a => a.getAttribute("href").replace(/[?#].*/, "")))],
                problems: (width, height) => {
                    const problems = [];
                    if (document.documentElement.scrollWidth > width + 1) problems.push(`the page scrolls sideways (${document.documentElement.scrollWidth}px wide)`);
                    tappable(main(), width, height, problems);
                    legible(main(), problems);
                    window.scrollTo(0, 0);
                    return [...new Set(problems)];
                },
                within: (selector, width, height) => {
                    const problems = [];
                    const scope = document.querySelector(selector);
                    if (!scope) return [`${selector} is missing`];
                    tappable(scope, width, height, problems);
                    legible(scope, problems);
                    window.scrollTo(0, 0);
                    return [...new Set(problems)];
                },
                opened: (kind, width, height) => {
                    const surface = kind === "dialog" ? document.querySelector("dialog[open]") : document.querySelector("[popover]:popover-open");
                    if (!surface) return ["nothing opened"];
                    const problems = [];
                    const box = surface.getBoundingClientRect();
                    if (box.left < -1 || box.top < -1 || box.right > width + 1 || box.bottom > height + 1)
                        problems.push(`does not fit the screen (${Math.round(box.left)},${Math.round(box.top)} to ${Math.round(box.right)},${Math.round(box.bottom)})`);
                    tappable(surface, width, height, problems);
                    return [...new Set(problems)];
                },
                close: () => {
                    document.querySelectorAll("[popover]:popover-open").forEach(p => p.hidePopover());
                    document.querySelectorAll("dialog[open]").forEach(d => d.close());
                },
            };
        })();
        """;
}

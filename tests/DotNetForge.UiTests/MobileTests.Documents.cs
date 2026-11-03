using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>Word, Excel and PowerPoint files open in the media preview, in the app and not in a tab of their own.</summary>
public sealed partial class MobileTests
{
    public static TheoryData<string> PreviewDevices() => new("desktop", "small-phone", "phone", "tablet");

    private async Task<(IPage Page, int Width, int Height)> OpenMediaAsync(string device)
    {
        if (device == "desktop")
        {
            var desktop = await Host.NewPageAsync();
            await desktop.AddInitScriptAsync(script: AuditScript);
            await desktop.GotoAsync("/admin/media");
            return (desktop, 1280, 900);
        }

        var (width, height) = Devices[device];
        var page = await NewDevicePageAsync(device);
        await page.GotoAsync("/admin/media");
        return (page, width, height);
    }

    private static async Task OpenPreviewAsync(IPage page, string device, string file)
    {
        var name = page.Locator($"[data-preview-name='{file}'] .dnf-media-card__name");
        await name.ScrollIntoViewIfNeededAsync();
        if (device == "desktop")
        {
            await name.ClickAsync();
        }
        else
        {
            await name.TapAsync();
        }

        await Assertions.Expect(page.Locator("#media-preview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".dnf-media-preview__document")).Not.ToContainTextAsync("Opening the document");
    }

    [Theory]
    [MemberData(nameof(PreviewDevices))]
    public async Task A_word_document_opens_in_the_preview(string device)
    {
        var (page, width, height) = await OpenMediaAsync(device);
        await OpenPreviewAsync(page, device, "sample.docx");

        var document = page.Locator(".dnf-media-preview__page");
        await Assertions.Expect(document.Locator(".dnf-media-preview__heading--1").First).ToHaveTextAsync("Quarterly report");
        await Assertions.Expect(document.Locator("strong", new LocatorLocatorOptions { HasText = "twelve percent" })).ToBeVisibleAsync();
        await Assertions.Expect(document.Locator(".dnf-media-preview__item").First).ToContainTextAsync("First point");

        // A numbered list counts from one, also right after a bulleted list.
        await Assertions.Expect(document.Locator(".dnf-media-preview__item", new LocatorLocatorOptions { HasText = "Step one" }).Locator(".dnf-media-preview__marker")).ToHaveTextAsync("1.");
        await Assertions.Expect(document.Locator(".dnf-media-preview__item", new LocatorLocatorOptions { HasText = "Step two" }).Locator(".dnf-media-preview__marker")).ToHaveTextAsync("2.");
        await Assertions.Expect(document.Locator("td", new LocatorLocatorOptions { HasText = "North" })).ToBeVisibleAsync();

        // One inch in the document, though the file is 8 pixels wide.
        await Assertions.Expect(document.Locator("img")).ToBeVisibleAsync();
        Assert.Equal(96, (await document.Locator("img").BoundingBoxAsync())!.Width, 1);

        AssertClean(await ProblemsWithinAsync(page, ".dnf-media-preview__page", width, height));
        await FinishAsync(page);
    }

    [Theory]
    [MemberData(nameof(PreviewDevices))]
    public async Task A_workbook_opens_in_the_preview_with_a_tab_per_sheet(string device)
    {
        var (page, width, height) = await OpenMediaAsync(device);
        await OpenPreviewAsync(page, device, "sample.xlsx");

        var sheet = page.Locator(".dnf-media-preview__sheet");
        await Assertions.Expect(sheet.Locator("td", new LocatorLocatorOptions { HasText = "2026-03-31" })).ToBeVisibleAsync();
        await Assertions.Expect(sheet.Locator("td", new LocatorLocatorOptions { HasText = "25%" })).ToBeVisibleAsync();

        var notes = page.Locator(".dnf-media-preview__sheet-tabs button", new PageLocatorOptions { HasText = "Notes" });
        if (device == "desktop")
        {
            await notes.ClickAsync();
        }
        else
        {
            await notes.TapAsync();
        }

        await Assertions.Expect(sheet.Locator("td", new LocatorLocatorOptions { HasText = "Far corner" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".dnf-media-preview__sheet-tabs button", new PageLocatorOptions { HasText = "Hidden" })).ToHaveCountAsync(0);
        AssertClean(await ProblemsWithinAsync(page, ".dnf-media-preview__document", width, height));
        await FinishAsync(page);
    }

    [Theory]
    [MemberData(nameof(PreviewDevices))]
    public async Task A_presentation_opens_in_the_preview_slide_by_slide(string device)
    {
        var (page, width, _) = await OpenMediaAsync(device);
        await OpenPreviewAsync(page, device, "sample.pptx");

        var slides = page.Locator(".dnf-media-preview__slide");
        await Assertions.Expect(slides).ToHaveCountAsync(4); // the hidden slide stays hidden
        await Assertions.Expect(slides.First).ToContainTextAsync("Quarterly review");
        await Assertions.Expect(slides.Nth(1).Locator(".dnf-media-preview__item").First).ToContainTextAsync("Sales grew");
        await Assertions.Expect(slides.Nth(2).Locator("img")).ToBeVisibleAsync();
        await Assertions.Expect(slides.Nth(3)).ToContainTextAsync("Chart not shown"); // and it says so, here and above the slides
        await Assertions.Expect(page.Locator(".dnf-media-preview__note")).ToBeVisibleAsync();

        var box = (await slides.First.BoundingBoxAsync())!;
        Assert.InRange(box.X, 0, width);
        Assert.True(box.X + box.Width <= width, $"The slide runs off a {width}px screen: {box.X}+{box.Width}.");

        // A table stays readable on a dark slide.
        var table = slides.Nth(2).Locator(".dnf-table");
        Assert.Equal("rgb(255, 255, 255)", await table.EvaluateAsync<string>("t => getComputedStyle(t).backgroundColor"));
        await FinishAsync(page);
    }

    /// <summary>Stayed on this tab, broke no Content-Security-Policy rule, and closes.</summary>
    private async Task FinishAsync(IPage page)
    {
        Assert.Single(page.Context.Pages);
        Assert.DoesNotContain(Host.ConsoleErrors, e => e.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase));
        await page.Locator("[data-preview-close]").ClickAsync();
        await Assertions.Expect(page.Locator(".dnf-media-preview__document")).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }
}

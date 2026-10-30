using DotNetForge.IntegrationTests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace DotNetForge.UiTests;

/// <summary>
/// One running CMS for the browser tests (.docs/guides/testing.md#ui-tests): the integration tests' factory on a real
/// Kestrel port (the browser needs a socket), installed, plus Chromium and a signed-in Super Admin session that every
/// test page reuses. Shared by the whole test collection; disposed after it.
/// </summary>
public sealed class UiHost : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private string? _storageState;

    public DotNetForgeWebFactory Factory { get; } = new();

    public IBrowser Browser { get; private set; } = default!;

    /// <summary>The app's root URL, e.g. http://127.0.0.1:53211.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Factory.UseKestrel(0);
        Factory.StartServer();
        var addresses = Factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        BaseUrl = addresses.Addresses.First().Replace("[::]", "127.0.0.1", StringComparison.Ordinal).TrimEnd('/');
        await Factory.InstallAsync();

        _playwright = await Playwright.CreateAsync();
        try
        {
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException e) when (e.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Chromium for Playwright is not installed. Once per machine, after a build: " +
                "pwsh tests/DotNetForge.UiTests/bin/Debug/net10.0/playwright.ps1 install chromium " +
                "(.docs/guides/testing.md#ui-tests).", e);
        }

        // Sign in once through the real form; every page starts from this session.
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/login");
        await page.Locator("input[name=email]").FillAsync(DotNetForgeWebFactory.AdminEmail);
        await page.Locator("input[name=password]").FillAsync(DotNetForgeWebFactory.AdminPassword);
        await page.Locator("form [type=submit]").First.ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath.StartsWith("/admin", StringComparison.Ordinal));
        _storageState = await context.StorageStateAsync();
        await context.CloseAsync();
    }

    /// <summary>
    /// A signed-in page (or a visitor's, with <paramref name="signedIn"/> false) in a fixed viewport, colour scheme and
    /// locale, without motion, so screenshots are repeatable. Screenshots use a tall viewport, so no section is taller
    /// than the window and the sticky top bar never covers one. With <paramref name="touch"/> the page is a touch device
    /// (<c>hover: none</c>, tap events, the viewport meta tag applies).
    /// </summary>
    public async Task<IPage> NewPageAsync(ColorScheme scheme = ColorScheme.Light, bool signedIn = true, int width = 1280, int height = 900, bool touch = false, bool motion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            StorageState = signedIn ? _storageState : null,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            ColorScheme = scheme,
            ReducedMotion = motion ? ReducedMotion.NoPreference : ReducedMotion.Reduce,
            Locale = "en-US",
            TimezoneId = "UTC",
        });
        var page = await context.NewPageAsync();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                ConsoleErrors.Add($"{page.Url}: {message.Text}");
            }
        };
        return page;
    }

    /// <summary>Browser console errors seen by any page (CSP violations, failed requests, script errors).</summary>
    public List<string> ConsoleErrors { get; } = new();

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class UiCollection : ICollectionFixture<UiHost>
{
    public const string Name = "UI";
}

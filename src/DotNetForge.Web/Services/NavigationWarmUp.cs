using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DotNetForge.Web.Services;

/// <summary>
/// Navigation warm-up (.docs/architecture/pages.md#navigation). A screen is slow the first time after the app starts,
/// while its code is compiled, and a click that lands just as a slow screen arrives can get lost. The admin layout
/// carries <see cref="StartId"/>; once per id, <c>nav-state.js</c> loads the other sidebar screens in the background
/// with the <see cref="Header"/> header, so they are quick by the time someone clicks.
/// </summary>
public static class NavigationWarmUp
{
    /// <summary>New with every start of the app.</summary>
    public static readonly string StartId = Guid.NewGuid().ToString("N")[..12];

    public const string Header = "X-Dnf-Purpose";

    public const string Value = "warm-up";

    /// <summary>navigation.js loading a screen ahead, while the pointer rests on its link.</summary>
    public const string Prefetch = "prefetch";

    /// <summary>A load nobody is looking at yet: the warm-up or a prefetch.</summary>
    public static bool IsBackground(HttpRequest request) =>
        request.Headers[Header].ToString() is Value or Prefetch;
}

/// <summary>
/// A background load (<see cref="NavigationWarmUp.IsBackground"/>) renders without the flash messages (TempData): it
/// starts empty and saves nothing, so it can never take the toast meant for the screen the user opens next. Every
/// other request gets the usual TempData.
/// </summary>
public sealed class WarmUpTempDataFactory(ITempDataProvider provider) : ITempDataDictionaryFactory
{
    private static readonly object Key = new();
    private readonly TempDataDictionaryFactory _usual = new(provider);

    public ITempDataDictionary GetTempData(HttpContext context)
    {
        if (!NavigationWarmUp.IsBackground(context.Request))
        {
            return _usual.GetTempData(context);
        }

        if (context.Items.TryGetValue(Key, out var existing) && existing is ITempDataDictionary tempData)
        {
            return tempData;
        }

        var empty = new TempDataDictionary(context, NoTempData.Instance);
        context.Items[Key] = empty;
        return empty;
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public static readonly NoTempData Instance = new();

        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}

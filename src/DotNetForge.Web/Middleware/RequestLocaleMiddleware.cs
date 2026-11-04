using System.Globalization;
using DotNetForge.Web.Services.Localization;

namespace DotNetForge.Web.Middleware;

/// <summary>
/// Resolves the request's locale (.docs/features/internationalization.md#request-locale), before routing:
/// <list type="bullet">
/// <item>a path starting with an enabled locale other than the default (<c>/de/about</c>) is that locale; the prefix is
/// taken off the path, so every route answers it as usual, and <see cref="RequestLocale.Url"/> puts it back on links;</item>
/// <item>a path starting with the default locale's code (<c>/en/about</c>) is redirected to the unprefixed URL (one URL per
/// page and language);</item>
/// <item>anything else is the default locale.</item>
/// </list>
/// The admin, the API, sign-in and setup are not localized by URL: <c>/de/admin</c> or <c>/de/api/...</c> stay as they are
/// (and answer 404).
/// Only the UI-text culture (<see cref="CultureInfo.CurrentUICulture"/>) follows the locale; number and date formatting
/// stay culture-neutral, so form values, JSON and generated CSS read the same in every language.
/// </summary>
public sealed class RequestLocaleMiddleware(RequestDelegate next, LocaleCatalog catalog)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var locales = await catalog.GetAsync(context.RequestAborted);
        var locale = locales.Default;
        var (first, rest) = SplitFirstSegment(context.Request.Path.Value);
        if (first is not null && !IsUnlocalized(rest) && locales.FindEnabled(first) is { } prefixed)
        {
            if (prefixed.IsDefault)
            {
                if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                {
                    context.Response.Redirect(rest + context.Request.QueryString, permanent: true);
                    return;
                }
            }
            else
            {
                locale = prefixed;
                context.Request.Path = rest;
            }
        }

        var uiLanguage = locale.Code;
        context.Features.Set(new RequestLocale(locale, locales, uiLanguage));
        CultureInfo.CurrentUICulture = Culture(uiLanguage);
        await next(context);
    }

    private static readonly string[] Unlocalized = { "/admin", "/api", "/account", "/login", "/register", "/forgot-password", "/reset-password", "/confirm-email", "/setup", "/health", "/media" };

    /// <summary>Paths that have no localized form: the admin, the API, sign-in and setup.</summary>
    private static bool IsUnlocalized(string path) =>
        Unlocalized.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase) && (path.Length == p.Length || path[p.Length] == '/'));

    /// <summary><c>/de/about</c> → (<c>de</c>, <c>/about</c>); <c>/de</c> → (<c>de</c>, <c>/</c>); no segment → (null, path).</summary>
    private static (string? First, string Remainder) SplitFirstSegment(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return (null, path ?? "/");
        }

        var end = path.IndexOf('/', 1);
        return end < 0 ? (path[1..], "/") : (path[1..end], path[end..]);
    }

    private static CultureInfo Culture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}

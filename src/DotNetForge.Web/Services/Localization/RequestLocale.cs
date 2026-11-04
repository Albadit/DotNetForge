namespace DotNetForge.Web.Services.Localization;

/// <summary>
/// The locale of the current request (.docs/features/internationalization.md#request-locale), set by
/// <c>RequestLocaleMiddleware</c>: the public site's locale from the URL prefix (<c>/de/about</c> → German; no prefix →
/// the default), and the admin's UI language. Views read it through <see cref="Of"/>.
/// </summary>
public sealed class RequestLocale
{
    public RequestLocale(LocaleInfo locale, LocaleSet locales, string uiLanguage)
    {
        Locale = locale;
        Locales = locales;
        UiLanguage = uiLanguage;
    }

    /// <summary>The content locale: the URL's, else the default.</summary>
    public LocaleInfo Locale { get; }

    public LocaleSet Locales { get; }

    /// <summary>The language of the UI text (the admin language in the admin, else <see cref="Locale"/>), for <c>&lt;html lang&gt;</c>.</summary>
    public string UiLanguage { get; }

    /// <summary>Whether this is a translation (not the default locale).</summary>
    public bool IsTranslation => !Locale.IsDefault;

    /// <summary>The URL prefix of <see cref="Locale"/>: <c>/de</c>, or empty for the default locale.</summary>
    public string Prefix => PrefixOf(Locale);

    /// <summary>A site path in this request's locale: <c>/about</c> → <c>/de/about</c>; <c>/</c> → <c>/de</c>.</summary>
    public string Url(string path) => UrlIn(Locale, path);

    /// <summary>A site path in <paramref name="locale"/> (the default locale has no prefix).</summary>
    public static string UrlIn(LocaleInfo locale, string path)
    {
        var prefix = PrefixOf(locale);
        if (prefix.Length == 0)
        {
            return path;
        }

        return path is "" or "/" ? prefix : prefix + (path.StartsWith('/') ? path : "/" + path);
    }

    private static string PrefixOf(LocaleInfo locale) => locale.IsDefault ? string.Empty : "/" + locale.Code;

    /// <summary>The request's locale; English with no translations when the middleware did not run (tests of a single view).</summary>
    public static RequestLocale Of(HttpContext context) =>
        context.Features.Get<RequestLocale>() ?? new RequestLocale(Fallback.Default, Fallback, "en");

    private static readonly LocaleSet Fallback = new(Array.Empty<LocaleInfo>());
}

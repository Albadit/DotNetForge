namespace DotNetForge.Shared.Content;

/// <summary>
/// The current page in one enabled locale, for a language switcher and <c>hreflang</c> links
/// (.docs/features/internationalization.md#public-site). Views get the list as <c>ViewData["Languages"]</c>.
/// </summary>
/// <param name="Code">The locale code (<c>de-DE</c>), also the <c>hreflang</c> value.</param>
/// <param name="Name">The locale's display name ("Deutsch").</param>
/// <param name="Url">The page's path in that locale (<c>/de/about</c>; the default locale has no prefix).</param>
/// <param name="Current">This is the locale of the request.</param>
public sealed record LanguageLink(string Code, string Name, string Url, bool Current);

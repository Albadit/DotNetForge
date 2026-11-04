using System.Globalization;
using DotNetForge.Web.Services.Localization;

namespace DotNetForge.Web.Middleware;

/// <summary>
/// Picks the admin language of an <c>/admin</c> request (.docs/features/internationalization.md#admin-language), after
/// authentication so the user's own choice counts: <see cref="AdminText"/> shows that language, the UI-text culture and
/// <see cref="RequestLocale.UiLanguage"/> (<c>&lt;html lang&gt;</c>) follow it. Other requests keep the content locale,
/// and the UI docs (<c>/admin/docs/ui</c>) stay English: they are reference material written in English.
/// </summary>
public sealed class AdminLanguageMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AdminLanguageService languages, AdminText text)
    {
        if (context.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Path.StartsWithSegments("/admin/docs/ui", StringComparison.OrdinalIgnoreCase))
        {
            var language = await languages.ResolveAsync(context.User, context.RequestAborted);
            text.Use(language.Code);
            var current = RequestLocale.Of(context);
            context.Features.Set(new RequestLocale(current.Locale, current.Locales, language.Code));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language.Code);
        }

        await next(context);
    }
}

using DotNetForge.Data;
using DotNetForge.Shared.Content;
using DotNetForge.Web.Services;
using DotNetForge.Web.Services.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// The stylesheet of a live page's published layout (.docs/features/page-builder.md#rendering): an external,
/// cacheable file instead of an inline &lt;style&gt;, so the Content-Security-Policy keeps <c>style-src 'self'</c>.
/// The URL carries a hash of the layout (<c>?v=</c>), so a matching request is cached for a year; any change to the
/// layout changes the URL. Under a locale's prefix (<c>/de/page-css/{id}.css</c>) it is the stylesheet of that
/// translation's layout, when the translation has one (.docs/features/internationalization.md#content-pages).
/// </summary>
public sealed class PageCssController : Controller
{
    private readonly DotNetForgeDbContext _db;

    public PageCssController(DotNetForgeDbContext db)
    {
        _db = db;
    }

    [HttpGet("/page-css/{id:guid}.css")]
    public async Task<IActionResult> Get(Guid id, string? v)
    {
        var live = await _db.Pages.AsNoTracking().Live(DateTime.UtcNow)
            .Where(p => p.Id == id)
            .Select(p => new { p.LayoutJson })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        var json = live?.LayoutJson;
        var locale = RequestLocale.Of(HttpContext);
        if (live is not null && locale.IsTranslation)
        {
            json = await _db.PageTranslations.AsNoTracking()
                .Where(t => t.PageId == id && t.LocaleId == locale.Locale.Id)
                .Select(t => t.LayoutJson)
                .FirstOrDefaultAsync(HttpContext.RequestAborted) ?? json;
        }
        if (PageBuilderService.Css(json) is not { } css)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = v is not null && v == PageBuilderService.Hash(json!)
            ? "public, max-age=31536000, immutable"
            : "public, max-age=60";
        return Content(css, "text/css; charset=utf-8");
    }
}

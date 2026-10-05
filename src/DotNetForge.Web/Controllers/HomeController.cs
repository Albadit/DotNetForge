using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>The public frontend. Renders independently of the admin area (.docs/features/themes.md).</summary>
public sealed class HomeController : Controller
{
    private readonly AppEnvironment _env;
    private readonly DotNetForgeDbContext _db;

    public HomeController(AppEnvironment env, DotNetForgeDbContext db)
    {
        _env = env;
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["AppName"] = _env.AppName;
        var now = DateTime.UtcNow;

        // The site root renders the root page (slug "/", or legacy "home") when live; else the list.
        var home = await Live(_db.Pages.AsNoTracking().Where(p => p.Slug == "/" || p.Slug == "home"), now)
            .OrderBy(p => p.Slug == "/" ? 0 : 1)
            .FirstOrDefaultAsync();
        if (home is not null)
        {
            return View("Page", home);
        }

        var publishedPages = await Live(_db.Pages.AsNoTracking(), now)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Title, p.Slug })
            .ToListAsync();

        ViewData["PublishedCount"] = publishedPages.Count;
        return View(publishedPages.Select(p => (p.Title, p.Slug)).ToList());
    }

    /// <summary>
    /// Renders a live page by its URL. Wired as the catch-all fallback (Program.cs) so a public URL like
    /// <c>/about/team</c> resolves down the page tree. Only the tree shape (id, parent, slug) of live pages is loaded
    /// for the walk; the matched page is then loaded in full.
    /// </summary>
    public async Task<IActionResult> RenderPage()
    {
        var path = (Request.Path.Value ?? string.Empty).Trim('/');
        if (path.Length == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pages = await Live(_db.Pages.AsNoTracking(), DateTime.UtcNow)
            .Select(p => new { p.Id, p.ParentPageId, p.Slug })
            .ToListAsync(HttpContext.RequestAborted);

        // Walk the URL segment by segment down the page tree. Each segment matches a child by exact slug,
        // or by a dynamic "[param]" slug that captures any value (.docs/features/content-pages-and-routing.md).
        Guid? parentId = null;
        Guid? matchedId = null;
        var routeValues = new Dictionary<string, string>();

        foreach (var segment in segments)
        {
            var exact = pages.FirstOrDefault(p => p.ParentPageId == parentId &&
                string.Equals(p.Slug, segment, StringComparison.OrdinalIgnoreCase));
            var dynamic = exact is null
                ? pages.FirstOrDefault(p => p.ParentPageId == parentId &&
                    p.Slug.StartsWith('[') && p.Slug.EndsWith(']'))
                : null;

            var match = exact ?? dynamic;
            if (match is null)
            {
                return NotFound();
            }

            if (exact is null)
            {
                routeValues[match.Slug.Trim('[', ']')] = segment;
            }

            parentId = matchedId = match.Id;
        }

        var matched = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == matchedId, HttpContext.RequestAborted);
        if (matched is null)
        {
            return NotFound();
        }

        ViewData["AppName"] = _env.AppName;
        ViewData["RouteValues"] = routeValues;
        return View("Page", matched);
    }

    /// <summary>
    /// The exception handler re-executes the failed request here with its original HTTP method, so this action
    /// must accept every method (a GET-only error action turns failed form posts into empty 500 responses).
    /// </summary>
    [Route("/error")]
    public IActionResult Error() => View();

    /// <summary>
    /// A page is publicly live only when published, not disabled, and within its scheduled window: the
    /// scheduled publish date has been reached (or none set) and the scheduled unpublish date has not
    /// (or none set). Evaluated per request, so a future "Scheduled publish" hides the page until that
    /// moment even though Published is checked (.docs/features/content-pages-and-routing.md).
    /// </summary>
    private static IQueryable<Page> Live(IQueryable<Page> pages, DateTime now) =>
        pages.Where(p => p.Published && !p.Disabled
            && (p.ScheduledPublishDate == null || p.ScheduledPublishDate <= now)
            && (p.ScheduledUnpublishDate == null || p.ScheduledUnpublishDate > now));
}

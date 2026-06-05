using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>The public frontend. Renders independently of the admin area (themes.md).</summary>
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

        // The site root renders the root page (slug "/", or legacy "home") when published; else the list.
        var home = await _db.Pages
            .AsNoTracking()
            .Where(p => (p.Slug == "/" || p.Slug == "home") && p.Published && !p.Disabled)
            .OrderBy(p => p.Slug == "/" ? 0 : 1)
            .FirstOrDefaultAsync();
        if (home is not null)
        {
            return View("Page", home);
        }

        var publishedPages = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Published && !p.Disabled)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Title, p.Slug })
            .ToListAsync();

        ViewData["PublishedCount"] = publishedPages.Count;
        return View(publishedPages.Select(p => (p.Title, p.Slug)).ToList());
    }

    /// <summary>
    /// Renders a published page by its slug. Wired as the catch-all fallback (Program.cs) so a public
    /// URL like <c>/home</c> resolves to the matching <see cref="Page"/>. Foundation-level: it resolves
    /// by the leaf slug and renders the page's metadata - the page builder / themes are future work.
    /// </summary>
    public async Task<IActionResult> RenderPage()
    {
        var path = (Request.Path.Value ?? string.Empty).Trim('/');
        if (path.Length == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pages = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Published && !p.Disabled)
            .ToListAsync();

        // Walk the URL segment by segment down the page tree. Each segment matches a child by exact slug,
        // or by a dynamic "[param]" slug that captures any value (content_manager.md / dynamic_routes.md).
        Guid? parentId = null;
        Page? matched = null;
        var routeValues = new Dictionary<string, string>();

        foreach (var segment in segments)
        {
            var exact = pages.FirstOrDefault(p => p.ParentPageId == parentId &&
                string.Equals(p.Slug, segment, StringComparison.OrdinalIgnoreCase));
            var dynamic = exact is null
                ? pages.FirstOrDefault(p => p.ParentPageId == parentId &&
                    p.Slug.StartsWith('[') && p.Slug.EndsWith(']'))
                : null;

            matched = exact ?? dynamic;
            if (matched is null)
            {
                return NotFound();
            }

            if (exact is null && dynamic is not null)
            {
                routeValues[dynamic.Slug.Trim('[', ']')] = segment;
            }

            parentId = matched.Id;
        }

        if (matched is null)
        {
            return NotFound();
        }

        ViewData["AppName"] = _env.AppName;
        ViewData["RouteValues"] = routeValues;
        return View("Page", matched);
    }

    [HttpGet("/error")]
    public IActionResult Error() => View();
}

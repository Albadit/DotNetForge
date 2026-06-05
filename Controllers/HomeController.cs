using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
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
        var publishedPages = await _db.Pages
            .AsNoTracking()
            .Where(p => p.Published && !p.Disabled)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Title, p.Slug })
            .ToListAsync();

        ViewData["PublishedCount"] = publishedPages.Count;
        return View(publishedPages.Select(p => (p.Title, p.Slug)).ToList());
    }

    [HttpGet("/error")]
    public IActionResult Error() => View();
}

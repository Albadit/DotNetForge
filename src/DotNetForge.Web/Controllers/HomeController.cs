using System.Security.Claims;
using DotNetForge.Abstractions.Authorization;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Components;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// The public frontend. Renders independently of the admin area, with the active theme's views when a theme
/// extension is enabled, else the built-in ones (.docs/features/themes.md).
/// </summary>
public sealed class HomeController : Controller
{
    private readonly AppEnvironment _env;
    private readonly DotNetForgeDbContext _db;
    private readonly ThemeResolver _themes;
    private readonly IPublishedPages _published;
    private readonly ExtensionViewRenderer _renderer;
    private readonly PagePermissionService _pagePermissions;
    private readonly IPermissionService _permissions;
    private readonly ContentRenderService _content;

    public HomeController(AppEnvironment env, DotNetForgeDbContext db, ThemeResolver themes, IPublishedPages published,
        ExtensionViewRenderer renderer, PagePermissionService pagePermissions, IPermissionService permissions, ContentRenderService content)
    {
        _content = content;
        _env = env;
        _db = db;
        _themes = themes;
        _published = published;
        _renderer = renderer;
        _pagePermissions = pagePermissions;
        _permissions = permissions;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["AppName"] = _env.AppName;
        var now = DateTime.UtcNow;

        // The site root renders the root page (slug "/", or legacy "home") when live; else the list.
        var home = await _db.Pages.AsNoTracking().Where(p => p.Slug == "/" || p.Slug == "home").Live(now)
            .OrderBy(p => p.Slug == "/" ? 0 : 1)
            .FirstOrDefaultAsync();
        if (home is not null)
        {
            return await ViewPageAsync(home);
        }

        // A root page that exists but is switched off (or outside its schedule) is not found, like any page that is
        // not live. The list below is only for a site without a root page (a fresh install).
        if (await _db.Pages.AnyAsync(p => p.Slug == "/" || p.Slug == "home"))
        {
            return NotFound();
        }

        var publishedPages = await _db.Pages.AsNoTracking().Live(now)
            .OrderBy(p => p.SortOrder)
            .Select(p => new { p.Title, p.Slug })
            .ToListAsync();

        ViewData["PublishedCount"] = publishedPages.Count;
        return await ThemedViewAsync("Index", publishedPages.Select(p => (p.Title, p.Slug)).ToList());
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
        var pages = await _db.Pages.AsNoTracking().Live(DateTime.UtcNow)
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
        return await ViewPageAsync(matched);
    }

    /// <summary>
    /// A live page, if the visitor may see it (.docs/features/content-pages-and-routing.md#page-permissions): a page
    /// with view grants asks anonymous visitors to sign in, and answers 404 to signed-in users without a granted role
    /// (so its existence is not revealed).
    /// </summary>
    private async Task<IActionResult> ViewPageAsync(Page page)
    {
        var canView = await _pagePermissions.CanViewAsync(page.Id, User, CanEditPages, HttpContext.RequestAborted);
        if (!canView)
        {
            return User.Identity?.IsAuthenticated == true
                ? NotFound()
                : Redirect("/login?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
        }

        // Content lists (.docs/features/structured-content.md#content-lists): resolved here, not only in the view, so a
        // detail page whose URL names no published entry (/products/[slug]) is a 404 like any unknown address.
        if (page.LayoutJson is not null)
        {
            var content = await PageLayoutViewComponent.ResolveContentAsync(_content, page.LayoutJson, page,
                ViewData["RouteValues"] as IReadOnlyDictionary<string, string>, HttpContext);
            if (content.MissingEntry)
            {
                return NotFound();
            }

            HttpContext.Items[PageLayoutViewComponent.ContentItemKey] = content;
            ViewData["ContentTitle"] = content.EntryTitle;
        }

        return await ThemedViewAsync("Page", page, await _themes.PageThemeIdAsync(page, HttpContext.RequestAborted));
    }

    /// <summary>Admins who may edit content pages always see restricted pages.</summary>
    private bool CanEditPages() =>
        _permissions.IsGranted(User.FindAll(ClaimTypes.Role).Select(c => c.Value), PermissionAreas.CollectionTypes, PermissionActions.Update);

    /// <summary>
    /// The exception handler re-executes the failed request here with its original HTTP method, so this action
    /// must accept every method (a GET-only error action turns failed form posts into empty 500 responses).
    /// </summary>
    [Route("/error")]
    public IActionResult Error() => View();

    /// <summary>
    /// The active theme's <c>Views/{name}.cshtml</c> when it has one, else the built-in view. Both get <c>Menu</c>: the
    /// live pages marked "display in menu" at the top level, as <see cref="PublishedPage"/>s; theme views also get the
    /// extension view data (<c>Extension</c>, <c>Settings</c>, <c>ResourceBase</c>).
    /// </summary>
    private async Task<IActionResult> ThemedViewAsync(string name, object model, string? pageThemeId = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["Menu"] = (await _published.ListAsync(HttpContext.RequestAborted))
            .Where(p => p.DisplayInMenu && p.ParentPageId is null)
            .ToList();
        var theme = await _themes.GetActiveThemeAsync(HttpContext.RequestAborted, pageThemeId);
        if (theme?.FindView(name + ".cshtml") is not { } themeView)
        {
            return View(name, model);
        }

        ExtensionViewRenderer.AddExtensionViewData(ViewData, theme, admin: false);
        return View(_renderer.ToViewPath(themeView), model);
    }
}

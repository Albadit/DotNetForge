using DotNetForge.Data;
using DotNetForge.Extensions;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Pages (.docs/pages/pages.md): a two-pane page manager - the page tree on the left, the selected page's settings
/// form on the right. Structured content (content types and entries) is the Content Manager's (<see cref="ContentController"/>). Page rules live in <see cref="IPageService"/>; this controller
/// enforces who may do what (content permissions in the "Collection types" area: Authors create and edit their own
/// pages but cannot publish, reorder or touch other pages; Editors and Admins can do everything). The public site
/// derives liveness from the schedule at render time, so the form's Published checkbox is the author's intent only.
/// </summary>
[Route("admin/pages")]
[RequirePermission(PermissionAreas.CollectionTypes, PermissionActions.Read)]
public sealed class PagesController : AdminControllerBase
{
    private const string Area = PermissionAreas.CollectionTypes;

    private static readonly HashSet<string> Tabs = new(StringComparer.Ordinal) { "page", "seo", "theme", "permissions", "layout" };

    private readonly DotNetForgeDbContext _db;
    private readonly IPageService _pages;
    private readonly IAuditService _audit;
    private readonly PagePermissionService _pagePermissions;
    private readonly ExtensionCatalog _extensions;

    public PagesController(DotNetForgeDbContext db, IPageService pages, IAuditService audit, PagePermissionService pagePermissions,
        ExtensionCatalog extensions)
    {
        _db = db;
        _pages = pages;
        _audit = audit;
        _pagePermissions = pagePermissions;
        _extensions = extensions;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? selected)
    {
        return View(await BuildIndexAsync(selected, form: null));
    }

    /// <summary>Lets the tree offer drag-and-drop only to users who may reorder (the POST checks again).</summary>
    public override void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context)
    {
        ViewData["CanReorder"] = Can(Area, PermissionActions.Update);
        base.OnActionExecuted(context);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid? parent)
    {
        if (!Can(Area, PermissionActions.Create))
        {
            return Forbid();
        }

        if (parent is Guid parentId && !await _db.Pages.AnyAsync(p => p.Id == parentId))
        {
            return NotFound();
        }

        var siblings = await _db.Pages.CountAsync(p => p.ParentPageId == parent);
        // A short slug, unique among the siblings (slugs are unique per parent): new-page, new-page-2, ...
        var taken = (await _db.Pages
                .Where(p => p.ParentPageId == parent && p.Slug.StartsWith("new-page"))
                .Select(p => p.Slug)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slug = "new-page";
        for (var n = 2; taken.Contains(slug); n++)
        {
            slug = "new-page-" + n;
        }

        var page = new Page
        {
            CreatedById = CurrentUserId,
            Title = "Untitled page",
            Slug = slug,
            Published = false,
            PageType = Shared.Enums.PageType.Standard,
            ParentPageId = parent,
            SortOrder = siblings,
        };

        _db.Pages.Add(page);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentCreated, "Page", page.Id.ToString(), page.Title);
        return RedirectToAction(nameof(Index), new { selected = page.Id });
    }

    [HttpPost("update/{id:guid}")]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Saves the page form - all tabs at once: page settings, SEO, theme and (for users with <c>update</c> on every
    /// page) the page's permissions. <paramref name="permissionsShown"/> is posted only when the Permissions tab was
    /// editable, so an empty role list means "no grants" rather than "not shown".
    /// </summary>
    public async Task<IActionResult> Update(Guid id, PageInput form, List<Guid>? viewRoles, List<Guid>? editRoles, bool permissionsShown,
        List<UserPermissionInput>? userPermissions, string? tab)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null)
        {
            return NotFound();
        }

        if (!await CanEditPageAsync(page))
        {
            return Forbid();
        }

        var themeId = string.IsNullOrWhiteSpace(form.ThemeId) ? null : form.ThemeId.Trim();
        var error = ChangesPublishing(page, form) && !Can(Area, PermissionActions.Publish)
            ? "You don't have permission to publish, unpublish or schedule pages."
            : themeId is not null && themeId != page.ThemeId && !_extensions.Installed().Any(e => e.Manifest?.Type == "theme" && e.Manifest.Id == themeId)
                ? "That theme is not installed."
                : await _pages.ApplyAsync(page, form, HttpContext.RequestAborted);
        if (error is null && permissionsShown && CanManagePermissions)
        {
            var users = (userPermissions ?? new())
                .Where(u => u.UserId is not null)
                .Select(u => new PageUserGrant(u.UserId!.Value, Rule(u.View), Rule(u.Edit)))
                .ToList();
            error = await _pagePermissions.SaveAsync(page, viewRoles ?? new(), editRoles ?? new(), users, CurrentUserId, HttpContext.RequestAborted);
        }

        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            ViewData["Tab"] = Tabs.Contains(tab ?? "") ? tab : null;
            return View(nameof(Index), await BuildIndexAsync(id, form));
        }

        page.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentUpdated, "Page", page.Id.ToString(), page.Title);
        TempData["Success"] = $"Saved '{page.Title}'.";
        return RedirectToAction(nameof(Index), null, new { selected = id }, Tabs.Contains(tab ?? "") ? tab : null);
    }

    /// <summary>Changing who may see or edit a page needs <c>update</c> on every page, not only on pages one created.</summary>
    private bool CanManagePermissions => Can(Area, PermissionActions.Update);

    /// <summary>A user row of the Permissions tab: <c>allow</c>, <c>block</c> or empty (not set) per permission.</summary>
    public sealed class UserPermissionInput
    {
        public Guid? UserId { get; set; }
        public string? View { get; set; }
        public string? Edit { get; set; }
    }

    private static bool? Rule(string? value) => value switch { "allow" => true, "block" => false, _ => null };

    /// <summary>
    /// Copies a page (the tree's ⋯ → Duplicate): its settings, SEO, theme, layout (published and draft) and view/edit
    /// grants, titled "… (copy)" with a short free slug, placed right below the original among the same siblings and
    /// switched off. Child pages are not copied. Needs <c>create</c>; a user blocked from editing the page can't copy it.
    /// </summary>
    [HttpPost("duplicate/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(Guid id)
    {
        var source = await _db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (source is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Create) || await PageEditRuleAsync(source) == false)
        {
            return Forbid();
        }

        var siblings = await _db.Pages
            .Where(p => p.ParentPageId == source.ParentPageId)
            .ToListAsync();
        // A short slug, free among the siblings: about-copy, about-copy-2, ... (the root page "/" copies as home-copy;
        // a dynamic [id] page as id-copy - a parent may have only one dynamic child).
        var stem = source.Slug.Trim('/', '[', ']', ' ');
        stem = (stem.Length == 0 ? "home" : stem.Length > 180 ? stem[..180] : stem) + "-copy";
        var taken = siblings.Select(p => p.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slug = stem;
        for (var n = 2; taken.Contains(slug); n++)
        {
            slug = stem + "-" + n;
        }

        foreach (var sibling in siblings.Where(p => p.SortOrder > source.SortOrder))
        {
            sibling.SortOrder++;
        }

        var copy = new Page
        {
            CreatedById = CurrentUserId,
            Title = (source.Title.Length > 190 ? source.Title[..190] : source.Title) + " (copy)",
            Slug = slug,
            Published = false,
            ParentPageId = source.ParentPageId,
            SortOrder = source.SortOrder + 1,
            PageType = source.PageType,
            TargetUrl = source.TargetUrl,
            FileReference = source.FileReference,
            MetaTitle = source.MetaTitle,
            MetaDescription = source.MetaDescription,
            SeoKeywords = source.SeoKeywords,
            CanonicalUrl = source.CanonicalUrl,
            NoIndex = source.NoIndex,
            SocialImageUrl = source.SocialImageUrl,
            DisplayInMenu = source.DisplayInMenu,
            ThemeId = source.ThemeId,
            LayoutJson = source.LayoutJson,
            LayoutPublishedDate = source.LayoutPublishedDate,
            DraftLayoutJson = source.DraftLayoutJson,
            DraftVersion = source.DraftLayoutJson is null ? null : Guid.NewGuid(),
            DraftUpdatedDate = source.DraftUpdatedDate,
            DraftUpdatedById = source.DraftUpdatedById,
        };
        _db.Pages.Add(copy);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentCreated, "Page", copy.Id.ToString(), copy.Title, details: $"Duplicate of '{source.Title}'");

        // Keep the page's restrictions: a copy of a members-only page must not be open to everyone.
        var grants = await _pagePermissions.GetAsync(source.Id, HttpContext.RequestAborted);
        string? grantError = null;
        if (grants.View.Count > 0 || grants.Edit.Count > 0 || grants.Users.Count > 0)
        {
            grantError = await _pagePermissions.SaveAsync(copy, grants.View, grants.Edit, grants.Users, CurrentUserId, HttpContext.RequestAborted);
        }

        TempData["Success"] = $"Duplicated '{source.Title}'. The copy is switched off until you publish it.";
        if (grantError is not null)
        {
            TempData["Error"] = "The copy's permissions could not be copied: " + grantError + " Set them on its Permissions tab.";
        }

        return RedirectToAction(nameof(Index), new { selected = copy.Id });
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null)
        {
            return NotFound();
        }

        // A user blocked from editing this page can't delete it either.
        if (!CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, page.CreatedById) || await PageEditRuleAsync(page) == false)
        {
            return Forbid();
        }

        await _pagePermissions.RemoveAllAsync(page.Id, HttpContext.RequestAborted);
        var error = await _pages.DeleteAsync(page, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View(nameof(Index), await BuildIndexAsync(id, form: null));
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentDeleted, "Page", id.ToString(), page.Title);
        TempData["Success"] = $"Deleted '{page.Title}'.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Persists a drag-and-drop reorder/reparent from the tree (parentPageId + sortOrder per page).</summary>
    [HttpPost("reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder([FromBody] ReorderRequest request)
    {
        if (!Can(Area, PermissionActions.Update))
        {
            return Forbid();
        }

        var positions = request.Items.Select(i => new PagePosition(i.Id, i.ParentPageId, i.SortOrder)).ToList();
        var error = await _pages.ReorderAsync(positions, HttpContext.RequestAborted);
        if (error is not null)
        {
            return BadRequest(new { error });
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentReordered, "Page", entityDisplay: $"{positions.Count} page(s)");
        return Ok(new { ok = true });
    }

    /// <summary>Whether the form changes anything that decides when the page is public.</summary>
    private static bool ChangesPublishing(Page page, PageInput form) =>
        form.Published != page.Published ||
        PageService.ToUtc(form.ScheduledPublishDate) != page.ScheduledPublishDate ||
        PageService.ToUtc(form.ScheduledUnpublishDate) != page.ScheduledUnpublishDate;

    private async Task<ContentIndexViewModel> BuildIndexAsync(Guid? selectedId, PageInput? form)
    {
        var pages = await _db.Pages
            .AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .ToListAsync();

        var selectedForm = form;
        var selectedPage = selectedId is Guid id ? pages.FirstOrDefault(p => p.Id == id) : null;
        if (selectedForm is null && selectedPage is not null)
        {
            selectedForm = PageInput.From(selectedPage);
        }

        PageEditorExtras? extras = null;
        if (selectedPage is not null)
        {
            var grants = await _pagePermissions.GetAsync(selectedPage.Id, HttpContext.RequestAborted);
            var roles = await _db.Roles.AsNoTracking()
                .OrderByDescending(r => r.IsBuiltIn).ThenBy(r => r.Name)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();
            // Super Admins always have full access to every page: they are not listed (role or users).
            var superAdminRole = roles.FirstOrDefault(r => r.Name == Roles.SuperAdmin)?.Id;
            var superAdmins = superAdminRole is { } sa
                ? (await _db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == sa).Select(ur => ur.UserId).ToListAsync()).ToHashSet()
                : new HashSet<Guid>();
            var users = (await _db.Users.AsNoTracking()
                    .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName })
                    .ToListAsync())
                .Where(u => !superAdmins.Contains(u.Id))
                .Select(u => new PermissionUserOption(u.Id, u.Email, $"{u.FirstName} {u.LastName}".Trim()))
                .OrderBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var active = (await _extensions.GetActiveAsync("theme", HttpContext.RequestAborted)).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var themes = _extensions.Installed()
                .Where(e => e.Manifest?.Type == "theme" && e.Manifest.Id is not null)
                .Select(e => new ThemeOption(e.Manifest!.Id!, e.Manifest.Name ?? e.Manifest.Id!, e.Manifest.Description, active.Contains(e.Manifest.Id!)))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            extras = new PageEditorExtras
            {
                Roles = roles.Where(r => r.Id != superAdminRole).Select(r => new PermissionRoleOption(r.Id, r.Name, Roles.SiteOnly.Contains(r.Name, StringComparer.OrdinalIgnoreCase))).ToList(),
                ViewRoles = grants.View,
                EditRoles = grants.Edit,
                Users = users,
                UserGrants = grants.Users,
                CanManagePermissions = CanManagePermissions,
                Themes = themes,
                HasLayout = selectedPage.LayoutJson is not null,
                HasDraftChanges = PageBuilderService.HasPendingChanges(selectedPage),
                LayoutPublishedDate = selectedPage.LayoutPublishedDate,
                DraftUpdatedDate = selectedPage.DraftUpdatedDate,
                CanEditLayout = await CanEditPageAsync(selectedPage),
            };
        }

        var parentOptions = pages
            .Where(p => p.Id != selectedId)
            .Select(p => (p.Id, Label: $"{p.Title} (/{p.Slug.TrimStart('/')})"))
            .ToList();

        return new ContentIndexViewModel
        {
            Tree = BuildTree(pages, p => CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, p.CreatedById)),
            Selected = selectedForm,
            SelectedId = selectedId,
            Extras = extras,
            ParentOptions = parentOptions,
        };
    }

    private static List<PageTreeNode> BuildTree(IReadOnlyList<Page> pages, Func<Page, bool> canDelete)
    {
        var nodes = pages.ToDictionary(
            p => p.Id,
            p => new PageTreeNode
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Published = p.Published && !p.Disabled,
                CanDelete = canDelete(p),
            });

        var roots = new List<PageTreeNode>();
        foreach (var page in pages)
        {
            var node = nodes[page.Id];
            if (page.ParentPageId is Guid parentId && nodes.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        foreach (var root in roots)
        {
            SetViewUrls(root, parentPath: null, parentNote: null);
        }

        return roots;
    }

    /// <summary>
    /// The tree's "View on site" links: a page's public URL as the site resolves it (.docs/features/content-pages-and-routing.md):
    /// a top-level <c>/</c> or <c>home</c> page is <c>/</c>, a top-level page <c>/slug</c>, a child its parent's path plus
    /// <c>/slug</c>. Not on the site: a page switched off, below one that is, a dynamic <c>[param]</c> page (no single
    /// URL) and children of the root page. The schedule is not checked; a scheduled page shows a "not found" until then.
    /// </summary>
    private static void SetViewUrls(PageTreeNode node, string? parentPath, string? parentNote)
    {
        var slug = node.Slug.Trim('/');
        var isRoot = parentPath is null && (slug.Length == 0 || slug.Equals("home", StringComparison.OrdinalIgnoreCase));
        string? path = null;
        var note = parentNote;
        if (note is null && parentPath == "/")
        {
            note = "Not on the site: pages under the home page have no URL.";
        }
        else if (note is null && slug.StartsWith('[') && slug.EndsWith(']'))
        {
            note = "Not on the site as one page: its URL has a dynamic part.";
        }
        else if (note is null && !node.Published)
        {
            note = "Not on the site: this page is disabled.";
        }
        else if (note is null)
        {
            path = isRoot ? "/" : (parentPath is null ? string.Empty : parentPath.TrimEnd('/')) + "/" + slug;
        }

        node.ViewUrl = path;
        node.NotOnSite = path is null ? note : null;
        // Children inherit why their parent is off the site (a disabled parent hides them too).
        var childNote = path is null ? (note == "Not on the site: this page is disabled." ? "Not on the site: a parent page is disabled." : note) : null;
        foreach (var child in node.Children)
        {
            SetViewUrls(child, path ?? parentPath, childNote);
        }
    }
}

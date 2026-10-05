using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Content Manager (.docs/pages/content-manager.md): a two-pane page manager - the page tree on the left, the
/// selected page's settings form on the right. Page rules live in <see cref="IPageService"/>; this controller
/// enforces who may do what (content permissions in the "Collection types" area: Authors create and edit their own
/// pages but cannot publish, reorder or touch other pages; Editors and Admins can do everything). The public site
/// derives liveness from the schedule at render time, so the form's Published checkbox is the author's intent only.
/// </summary>
[Route("admin/content")]
public sealed class ContentController : AdminControllerBase
{
    private const string Area = PermissionAreas.CollectionTypes;

    private readonly DotNetForgeDbContext _db;
    private readonly IPageService _pages;
    private readonly IAuditService _audit;

    public ContentController(DotNetForgeDbContext db, IPageService pages, IAuditService audit)
    {
        _db = db;
        _pages = pages;
        _audit = audit;
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

        if (parent is Guid parentId && !await _db.Pages.AnyAsync(p => p.Id == parentId && p.TenantId == TenantId))
        {
            return NotFound();
        }

        var siblings = await _db.Pages.CountAsync(p => p.TenantId == TenantId && p.ParentPageId == parent);
        var page = new Page
        {
            TenantId = TenantId,
            CreatedById = CurrentUserId,
            Title = "Untitled page",
            Slug = "new-page-" + Guid.NewGuid().ToString("N")[..8],
            Published = false,
            Disabled = true,
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
    public async Task<IActionResult> Update(Guid id, PageInput form)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == TenantId);
        if (page is null)
        {
            return NotFound();
        }

        if (!CanModify(Area, PermissionActions.Update, PermissionActions.UpdateOwn, page.CreatedById))
        {
            return Forbid();
        }

        var error = ChangesPublishing(page, form) && !Can(Area, PermissionActions.Publish)
            ? "You don't have permission to publish, unpublish or schedule pages."
            : await _pages.ApplyAsync(page, form, TenantId, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View(nameof(Index), await BuildIndexAsync(id, form));
        }

        page.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ContentUpdated, "Page", page.Id.ToString(), page.Title);
        TempData["Success"] = $"Saved '{page.Title}'.";
        return RedirectToAction(nameof(Index), new { selected = id });
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == TenantId);
        if (page is null)
        {
            return NotFound();
        }

        if (!CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, page.CreatedById))
        {
            return Forbid();
        }

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
        var error = await _pages.ReorderAsync(positions, TenantId, HttpContext.RequestAborted);
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
            .Where(p => p.TenantId == TenantId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
            .ToListAsync();

        var selectedForm = form;
        if (selectedForm is null && selectedId is Guid id)
        {
            var page = pages.FirstOrDefault(p => p.Id == id);
            if (page is not null)
            {
                selectedForm = PageInput.From(page);
            }
        }

        var parentOptions = pages
            .Where(p => p.Id != selectedId)
            .Select(p => (p.Id, Label: $"{p.Title} (/{p.Slug.TrimStart('/')})"))
            .ToList();

        return new ContentIndexViewModel
        {
            Tree = BuildTree(pages),
            Selected = selectedForm,
            SelectedId = selectedId,
            ParentOptions = parentOptions,
        };
    }

    private static List<PageTreeNode> BuildTree(IReadOnlyList<Page> pages)
    {
        var nodes = pages.ToDictionary(
            p => p.Id,
            p => new PageTreeNode
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Published = p.Published,
                Disabled = p.Disabled,
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

        return roots;
    }
}

using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Content Manager (content_manager.md): a two-pane page manager - the page tree on the left, the selected
/// page's settings form on the right. Page validation/rules live in <see cref="PageService"/>; the public
/// site derives liveness from the schedule at render time (HomeController.Live), so the form's Published
/// checkbox is the author's intent only.
/// </summary>
[Route("admin/content")]
public sealed class ContentController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly PageService _pages;
    private readonly AuditService _audit;

    public ContentController(DotNetForgeDbContext db, PageService pages, AuditService audit)
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

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid? parent)
    {
        var siblings = await _db.Pages.CountAsync(p => p.TenantId == TenantId && p.ParentPageId == parent);
        var page = new Page
        {
            TenantId = TenantId,
            CreatedById = CurrentUserId,
            Title = "Untitled page",
            Slug = "new-page-" + Guid.NewGuid().ToString("N")[..6],
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
    public async Task<IActionResult> Update(Guid id, PageFormModel form)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == TenantId);
        if (page is null)
        {
            return NotFound();
        }

        var error = await _pages.ApplyAsync(page, form, TenantId, HttpContext.RequestAborted);
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

        // Never orphan children: re-parent them to the deleted page's parent.
        var children = await _db.Pages.Where(p => p.TenantId == TenantId && p.ParentPageId == id).ToListAsync();
        foreach (var child in children)
        {
            child.ParentPageId = page.ParentPageId;
        }

        _db.Pages.Remove(page);
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
        var ids = request.Items.Select(i => i.Id).ToList();
        var pages = await _db.Pages.Where(p => p.TenantId == TenantId && ids.Contains(p.Id)).ToListAsync();
        var byId = pages.ToDictionary(p => p.Id);

        foreach (var item in request.Items)
        {
            if (byId.TryGetValue(item.Id, out var page))
            {
                page.ParentPageId = item.ParentPageId;
                page.SortOrder = item.SortOrder;
                page.UpdatedDate = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private async Task<ContentIndexViewModel> BuildIndexAsync(Guid? selectedId, PageFormModel? form)
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
                selectedForm = ToForm(page);
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

    private static PageFormModel ToForm(Page p) => new()
    {
        Title = p.Title,
        Slug = p.Slug,
        MetaTitle = p.MetaTitle,
        MetaDescription = p.MetaDescription,
        SeoKeywords = p.SeoKeywords,
        CanonicalUrl = p.CanonicalUrl,
        Published = p.Published,
        Disabled = p.Disabled,
        DisplayInMenu = p.DisplayInMenu,
        ParentPageId = p.ParentPageId,
        SortOrder = p.SortOrder,
        PageType = p.PageType.ToString(),
        TargetUrl = p.TargetUrl,
        FileReference = p.FileReference,
        ScheduledPublishDate = p.ScheduledPublishDate,
        ScheduledUnpublishDate = p.ScheduledUnpublishDate,
    };
}

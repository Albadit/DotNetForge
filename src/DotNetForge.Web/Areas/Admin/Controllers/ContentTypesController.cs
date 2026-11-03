using DotNetForge.Content;
using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Content types (.docs/pages/content-types.md): the content types and the visual builder of each type's
/// fields. The rules live in <see cref="ContentTypeService"/>; this controller checks the <c>Content types</c> grants,
/// maps problems to the form and audits through the service. Entries are managed in the Content Manager
/// (<see cref="ContentController"/>).
/// </summary>
[Route("admin/content-types")]
[RequirePermission(PermissionAreas.ContentTypes, PermissionActions.Read)]
public sealed class ContentTypesController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly ContentTypeService _types;

    public ContentTypesController(DotNetForgeDbContext db, ContentTypeService types)
    {
        _db = db;
        _types = types;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return View(new ContentTypesIndexViewModel
        {
            Types = await CardsAsync(_db, HttpContext.RequestAborted),
            CanCreate = Can(PermissionAreas.ContentTypes, PermissionActions.Create),
            CanUpdate = Can(PermissionAreas.ContentTypes, PermissionActions.Update),
        });
    }

    /// <summary>The "new content type" screen: a name and a starting point (empty, or one of the templates).</summary>
    [HttpGet("new")]
    [RequirePermission(PermissionAreas.ContentTypes, PermissionActions.Create)]
    public IActionResult New() => View();

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.ContentTypes, PermissionActions.Create)]
    public async Task<IActionResult> Create(string? name, string? template)
    {
        var (type, error) = await _types.CreateAsync(name, template, CurrentUserId, HttpContext.RequestAborted);
        if (type is null)
        {
            ModelState.AddModelError(string.Empty, error!);
            return View(nameof(New));
        }

        TempData["Success"] = ContentTypeTemplates.Find(template) is { Requires.Count: > 0 }
            ? $"Created '{type.Name}' and the types it links to. Adjust the fields, then add entries."
            : $"Created '{type.Name}'. Add its fields, then save.";
        return Redirect($"/admin/content-types/{type.Id}");
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var type = await _types.FindAsync(id, HttpContext.RequestAborted);
        return type is null ? NotFound() : View(await BuildAsync(type, ContentTypeInput.From(type)));
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.ContentTypes, PermissionActions.Update)]
    public async Task<IActionResult> Save(Guid id, ContentTypeInput input)
    {
        var type = await _types.FindAsync(id, HttpContext.RequestAborted);
        if (type is null)
        {
            return NotFound();
        }

        var errors = await _types.UpdateAsync(type, input, HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            foreach (var e in errors)
            {
                ModelState.AddModelError(string.Empty, e);
            }

            // The posted state, so nothing typed into the builder is lost.
            return View(nameof(Edit), await BuildAsync(type, input));
        }

        TempData["Success"] = $"Saved '{type.Name}'.";
        return Redirect($"/admin/content-types/{type.Id}");
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.ContentTypes, PermissionActions.Delete)]
    public async Task<IActionResult> Delete(Guid id, bool withEntries = false)
    {
        var type = await _types.FindAsync(id, HttpContext.RequestAborted);
        if (type is null)
        {
            return NotFound();
        }

        var error = await _types.DeleteAsync(type, withEntries, HttpContext.RequestAborted);
        if (error is not null)
        {
            TempData["Error"] = error;
            return Redirect($"/admin/content-types/{type.Id}");
        }

        TempData["Success"] = $"Deleted '{type.Name}'.";
        return Redirect("/admin/content-types");
    }

    private async Task<ContentTypeEditViewModel> BuildAsync(ContentType type, ContentTypeInput input) => new()
    {
        Id = type.Id,
        Input = input,
        EntryCount = await _db.ContentEntries.CountAsync(e => e.ContentTypeId == type.Id, HttpContext.RequestAborted),
        RelationTargets = (await _types.ListAsync(HttpContext.RequestAborted)).Select(t => (t.Id, t.Name)).ToList(),
        CanSave = Can(PermissionAreas.ContentTypes, PermissionActions.Update),
        CanDelete = Can(PermissionAreas.ContentTypes, PermissionActions.Delete),
    };

    /// <summary>The types with field and entry counts (three single-table queries).</summary>
    internal static async Task<IReadOnlyList<ContentTypeCard>> CardsAsync(DotNetForgeDbContext db, CancellationToken ct)
    {
        var types = await db.ContentTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var entries = await db.ContentEntries.AsNoTracking()
            .Select(e => new { e.ContentTypeId, e.Published }).ToListAsync(ct);
        return types.Select(t => new ContentTypeCard(t.Id, t.Name, t.Slug, t.Description, t.Icon, t.Kind,
                ContentSchema.Parse(t.FieldsJson).Count,
                entries.Count(e => e.ContentTypeId == t.Id),
                entries.Count(e => e.ContentTypeId == t.Id && e.Published),
                t.UpdatedDate))
            .ToList();
    }
}

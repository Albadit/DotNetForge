using DotNetForge.Content;
using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// The Content Manager (.docs/pages/content-manager.md): structured content. An overview of the content types, an
/// entry list per type (search, status filter, sorting, paging, bulk delete, drag to reorder) and an entry form
/// generated from the type's fields. Rules are in <see cref="ContentEntryService"/>; this controller checks the
/// <c>Entries</c> grants (own-vs-any like pages and media). Types are defined on the Content types screen
/// (<see cref="ContentTypesController"/>); pages and their layouts on the Pages screen (<see cref="PagesController"/>).
/// Every screen needs <c>Entries</c> read, so the sidebar hides it from roles without it (e.g. Developer).
/// </summary>
[Route("admin/content")]
[RequirePermission(PermissionAreas.Entries, PermissionActions.Read)]
public sealed class ContentController : AdminControllerBase
{
    private const string Area = PermissionAreas.Entries;
    private const int PageSize = 25;
    private const int PickerSize = 100;

    private readonly DotNetForgeDbContext _db;
    private readonly ContentTypeService _types;
    private readonly ContentEntryService _entries;
    private readonly ContentQueryService _query;
    private readonly ContentRenderService _render;
    private readonly MediaService _media;

    public ContentController(DotNetForgeDbContext db, ContentTypeService types, ContentEntryService entries, ContentQueryService query,
        ContentRenderService render, MediaService media)
    {
        _db = db;
        _types = types;
        _entries = entries;
        _query = query;
        _render = render;
        _media = media;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? selected)
    {
        // Before structured content, /admin/content was the page tree: old links keep working.
        if (selected is { } page)
        {
            return RedirectPermanent($"/admin/pages?selected={page}");
        }

        return View(new ContentManagerViewModel
        {
            Types = await ContentTypesController.CardsAsync(_db, HttpContext.RequestAborted),
            CanManageTypes = Can(PermissionAreas.ContentTypes, PermissionActions.Create),
            CanCreateEntries = Can(Area, PermissionActions.Create),
        });
    }

    /// <summary>The page builder's old address (it moved with the page tree to /admin/pages).</summary>
    [HttpGet("{id:guid}/builder")]
    public IActionResult LegacyBuilder(Guid id) => RedirectPermanent($"/admin/pages/{id}/builder");

    // ---- Entry list -------------------------------------------------------------------------------------------

    [HttpGet("{type:regex(^[[a-z]][[a-z0-9-]]*$)}")]
    public async Task<IActionResult> List(string type, string? q, string? status, string? sort, bool desc = false, int page = 1)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Read))
        {
            return Forbid();
        }

        if (contentType.Kind == ContentTypeKind.Single)
        {
            var single = await _entries.SingleEntryAsync(contentType, HttpContext.RequestAborted);
            return Redirect(single is null ? $"/admin/content/{contentType.Slug}/new" : $"/admin/content/{contentType.Slug}/{single.Id}");
        }

        var fields = ContentSchema.Parse(contentType.FieldsJson);
        var columns = fields.Where(f => f.ShowInList && f.Key != contentType.TitleField).Take(4).Select(f => (f.Key, f.Label)).ToList();
        var sortKey = sort is "title" or "createdAt" or "updatedAt" or "publishedAt" || fields.Any(f => f.Key == sort && !FieldTypes.HasFields(f.Type)) ? sort! : "sortOrder";
        status = status is "draft" or "published" or "changed" ? status : null;
        var result = await _query.QueryAsync(new EntryQuery
        {
            Type = contentType,
            Search = q,
            Status = status,
            Sort = sortKey,
            Descending = desc,
            Page = page,
            PageSize = PageSize,
        }, HttpContext.RequestAborted);

        // Column text comes from the same item data the site renders (formatted numbers, dates, option labels...).
        var items = await _render.ItemsAsync(contentType, result.Items, string.Empty, published: false, HttpContext.RequestAborted);
        var rows = result.Items.Select((e, i) =>
        {
            var cells = (JObject)items[i]["fields"]!;
            return new EntryRow(e,
                columns.Select(c => (string?)(cells.Property(c.Key)?.Value as JObject)?["text"] ?? string.Empty).ToList(),
                CanModify(Area, PermissionActions.Update, PermissionActions.UpdateOwn, e.CreatedById),
                CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, e.CreatedById));
        }).ToList();

        return View(new EntryListViewModel
        {
            Type = contentType,
            Columns = columns,
            Rows = rows,
            Total = result.Total,
            Page = result.Page,
            PageCount = result.PageCount,
            Search = q,
            Status = status,
            Sort = sortKey,
            Descending = desc,
            CanCreate = Can(Area, PermissionActions.Create),
            CanPublish = Can(Area, PermissionActions.Publish),
            CanDelete = Can(Area, PermissionActions.Delete) || Can(Area, PermissionActions.DeleteOwn),
            CanReorder = Can(Area, PermissionActions.Update) && sortKey == "sortOrder" && !desc && string.IsNullOrWhiteSpace(q) && status is null,
            CanEditType = Can(PermissionAreas.ContentTypes, PermissionActions.Read),
        });
    }

    [HttpPost("{type}/delete-selected")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(string type, List<Guid> ids)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFound();
        }

        // Every selected entry must be deletable by this user; otherwise nothing is deleted.
        var owners = await _db.ContentEntries.AsNoTracking()
            .Where(e => e.ContentTypeId == contentType.Id && ids.Contains(e.Id))
            .Select(e => e.CreatedById).ToListAsync(HttpContext.RequestAborted);
        if (owners.Count == 0 || owners.Any(o => !CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, o)))
        {
            return owners.Count == 0 ? Redirect($"/admin/content/{contentType.Slug}") : Forbid();
        }

        var count = await _entries.DeleteManyAsync(contentType, ids, HttpContext.RequestAborted);
        TempData["Success"] = count == 1 ? "Deleted 1 entry." : $"Deleted {count} entries.";
        return Redirect($"/admin/content/{contentType.Slug}");
    }

    public sealed record ReorderRequestBody(List<Guid> Ids);

    [HttpPost("{type}/reorder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reorder(string type, [FromBody] ReorderRequestBody body)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Update))
        {
            return Forbid();
        }

        await _entries.ReorderAsync(contentType, body.Ids ?? new List<Guid>(), HttpContext.RequestAborted);
        return Json(new { ok = true });
    }

    // ---- Entry form ---------------------------------------------------------------------------------------------

    [HttpGet("{type}/new")]
    public async Task<IActionResult> New(string type)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Create))
        {
            return Forbid();
        }

        var fields = ContentSchema.Parse(contentType.FieldsJson);
        return View("Edit", await BuildFormAsync(contentType, fields, null, EntryData.Defaults(fields), null, conflict: false));
    }

    [HttpGet("{type}/{id:guid}")]
    public async Task<IActionResult> Edit(string type, Guid id)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        var entry = contentType is null ? null : await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
        if (contentType is null || entry is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Read))
        {
            return Forbid();
        }

        return View(await BuildFormAsync(contentType, ContentSchema.Parse(contentType.FieldsJson), entry,
            ContentEntryService.Parse(entry.DataJson), null, conflict: false));
    }

    /// <summary>
    /// Saves the form: <paramref name="intent"/> <c>publish</c> publishes too (needs <c>publish</c>); a type without
    /// drafts always publishes. A new entry is created on its first save.
    /// </summary>
    [HttpPost("{type}/new")]
    [HttpPost("{type}/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string type, Guid? id, string? intent, Guid? baseVersion, bool force = false)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFound();
        }

        ContentEntry? entry = null;
        if (id is { } entryId)
        {
            entry = await _entries.FindAsync(contentType, entryId, HttpContext.RequestAborted);
            if (entry is null)
            {
                return NotFound();
            }

            if (!CanModify(Area, PermissionActions.Update, PermissionActions.UpdateOwn, entry.CreatedById))
            {
                return Forbid();
            }
        }
        else if (!Can(Area, PermissionActions.Create))
        {
            return Forbid();
        }
        else if (contentType.Kind == ContentTypeKind.Single && await _entries.SingleEntryAsync(contentType, HttpContext.RequestAborted) is { } existing)
        {
            // A single type has one entry: a second "new" form (e.g. two tabs) edits it instead of adding another.
            return Redirect($"/admin/content/{contentType.Slug}/{existing.Id}");
        }

        var fields = ContentSchema.Parse(contentType.FieldsJson);
        var input = EntryForm.Read(fields, Request.Form);
        var publish = intent == "publish" || !contentType.Drafts;
        if (publish && !Can(Area, PermissionActions.Publish))
        {
            if (contentType.Drafts)
            {
                ModelState.AddModelError(string.Empty, "You don't have permission to publish entries. Save a draft instead.");
                return View("Edit", await BuildFormAsync(contentType, fields, entry, input, null, conflict: false, baseVersion));
            }

            // A type without drafts publishes on every save: without publish rights nothing can be saved.
            return Forbid();
        }

        var result = await _entries.SaveAsync(contentType, entry, input, publish, force ? null : baseVersion, CurrentUserId, HttpContext.RequestAborted);
        if (result.Conflict)
        {
            ModelState.AddModelError(string.Empty, "Someone else saved this entry while you were editing it. Your changes are below: save again to overwrite theirs, or reload to see them.");
            return View("Edit", await BuildFormAsync(contentType, fields, entry, input, null, conflict: true, entry!.Version));
        }

        if (!result.Saved)
        {
            foreach (var e in result.Errors.Select(e => e.Message).Distinct())
            {
                ModelState.AddModelError(string.Empty, e);
            }

            return View("Edit", await BuildFormAsync(contentType, fields, entry, input, result.Errors, conflict: false, baseVersion));
        }

        TempData["Success"] = publish ? $"Published '{ContentEntryService.Display(result.Entry!)}'." : $"Saved a draft of '{ContentEntryService.Display(result.Entry!)}'.";
        return Redirect($"/admin/content/{contentType.Slug}/{result.Entry!.Id}");
    }

    [HttpPost("{type}/{id:guid}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string type, Guid id, string? returnTo)
    {
        var (contentType, entry) = await LoadAsync(type, id);
        if (entry is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Publish))
        {
            return Forbid();
        }

        var errors = await _entries.PublishAsync(contentType!, entry, CurrentUserId, HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            TempData["Error"] = "Can't publish yet: " + string.Join(" ", errors.Select(e => e.Message).Distinct().Take(3));
        }
        else
        {
            TempData["Success"] = $"Published '{ContentEntryService.Display(entry)}'.";
        }

        return Redirect(returnTo == "list" ? $"/admin/content/{contentType!.Slug}" : $"/admin/content/{contentType!.Slug}/{entry.Id}");
    }

    [HttpPost("{type}/{id:guid}/unpublish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(string type, Guid id, string? returnTo)
    {
        var (contentType, entry) = await LoadAsync(type, id);
        if (entry is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Publish))
        {
            return Forbid();
        }

        await _entries.UnpublishAsync(contentType!, entry, HttpContext.RequestAborted);
        TempData["Success"] = $"Unpublished '{ContentEntryService.Display(entry)}'. It is no longer on the site or in the API.";
        return Redirect(returnTo == "list" ? $"/admin/content/{contentType!.Slug}" : $"/admin/content/{contentType!.Slug}/{entry.Id}");
    }

    [HttpPost("{type}/{id:guid}/duplicate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(string type, Guid id)
    {
        var (contentType, entry) = await LoadAsync(type, id);
        if (entry is null)
        {
            return NotFound();
        }

        if (!Can(Area, PermissionActions.Create) || contentType!.Kind == ContentTypeKind.Single)
        {
            return Forbid();
        }

        var copy = await _entries.DuplicateAsync(contentType, entry, CurrentUserId, HttpContext.RequestAborted);
        TempData["Success"] = $"Copied '{ContentEntryService.Display(entry)}'. The copy is a draft.";
        return Redirect($"/admin/content/{contentType.Slug}/{copy.Id}");
    }

    [HttpPost("{type}/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string type, Guid id)
    {
        var (contentType, entry) = await LoadAsync(type, id);
        if (entry is null)
        {
            return NotFound();
        }

        if (contentType!.Kind == ContentTypeKind.Single || !CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, entry.CreatedById))
        {
            return Forbid();
        }

        await _entries.DeleteAsync(contentType, entry, HttpContext.RequestAborted);
        TempData["Success"] = $"Deleted '{ContentEntryService.Display(entry)}'.";
        return Redirect($"/admin/content/{contentType.Slug}");
    }

    // ---- JSON for the form's pickers ----------------------------------------------------------------------------

    /// <summary>Entries of a type for relationship pickers: <c>[{ id, title, published }]</c>, title search.</summary>
    [HttpGet("api/entries")]
    public async Task<IActionResult> LookupEntries(Guid type, string? q)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null || !Can(Area, PermissionActions.Read))
        {
            return NotFound();
        }

        var page = await _query.QueryAsync(new EntryQuery { Type = contentType, Search = q, Sort = "title", PageSize = 50 }, HttpContext.RequestAborted);
        return Json(page.Items.Select(e => new { id = e.Id, title = ContentEntryService.Display(e), published = e.Published }));
    }

    /// <summary>Media for image and file pickers: <c>[{ id, name, url, isImage }]</c>, newest first.</summary>
    [HttpGet("api/media")]
    public async Task<IActionResult> LookupMedia(string? kind, string? q)
    {
        return Json(await MediaChoicesAsync(kind == "image", q, 200));
    }

    /// <summary>Uploads a file from an entry form (needs <c>Media</c> / <c>create</c>): <c>{ id, name, url, isImage }</c>.</summary>
    [HttpPost("api/media/upload")]
    [ValidateAntiForgeryToken]
    [MediaUploadLimit]
    public async Task<IActionResult> Upload(IFormFile? file, bool isPublic = true)
    {
        if (!Can(PermissionAreas.Media, PermissionActions.Create))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You don't have permission to upload files." });
        }

        if (file is null)
        {
            return BadRequest(new { message = "Choose a file to upload." });
        }

        var (stored, error) = await _media.UploadAsync(file, isPublic, CurrentUserId, reuseDuplicate: true, ct: HttpContext.RequestAborted);
        if (stored is null)
        {
            return BadRequest(new { message = error });
        }

        return Json(new MediaChoice(stored.Id, stored.OriginalName, MediaService.UrlFor(stored), stored.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)));
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    private async Task<(ContentType? Type, ContentEntry? Entry)> LoadAsync(string type, Guid id)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        var entry = contentType is null ? null : await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
        return (contentType, entry);
    }

    private async Task<IReadOnlyList<MediaChoice>> MediaChoicesAsync(bool imagesOnly, string? search, int take, IReadOnlyCollection<Guid>? include = null)
    {
        var rows = _db.MediaFiles.AsNoTracking();
        if (imagesOnly)
        {
            rows = rows.Where(m => m.ContentType.StartsWith("image/"));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(m => m.OriginalName.Contains(term));
        }

        var list = await rows.OrderByDescending(m => m.UploadedDate).Take(take)
            .Select(m => new { m.Id, m.OriginalName, m.FileName, m.ContentType }).ToListAsync(HttpContext.RequestAborted);
        var missing = include?.Where(id => list.All(m => m.Id != id)).ToList();
        if (missing is { Count: > 0 })
        {
            list.AddRange(await _db.MediaFiles.AsNoTracking().Where(m => missing.Contains(m.Id))
                .Select(m => new { m.Id, m.OriginalName, m.FileName, m.ContentType }).ToListAsync(HttpContext.RequestAborted));
        }

        return list.Select(m => new MediaChoice(m.Id, string.IsNullOrEmpty(m.OriginalName) ? m.FileName : m.OriginalName,
            MediaService.UrlFor(m.Id, m.FileName), m.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private async Task<EntryEditViewModel> BuildFormAsync(ContentType type, IReadOnlyList<FieldDefinition> fields, ContentEntry? entry, JObject values,
        IReadOnlyList<FieldError>? errors, bool conflict, Guid? baseVersion = null)
    {
        var ct = HttpContext.RequestAborted;
        var (mediaIds, entryRefs) = EntryData.References(fields, values);

        // Relationship options: the first entries of every linked type plus the ones already chosen.
        var relations = new Dictionary<Guid, IReadOnlyList<EntryChoice>>();
        var typeNames = new Dictionary<Guid, string>();
        foreach (var targetId in AllRelationTargets(fields).Distinct())
        {
            var target = await _types.FindAsync(targetId, ct);
            if (target is null)
            {
                continue;
            }

            typeNames[targetId] = target.Name;
            var chosen = entryRefs.Where(r => r.Type == targetId).Select(r => r.Entry).ToList();
            var options = await _db.ContentEntries.AsNoTracking()
                .Where(e => e.ContentTypeId == targetId)
                .OrderBy(e => e.Title).Take(PickerSize)
                .Select(e => new EntryChoice(e.Id, e.Title, e.Published)).ToListAsync(ct);
            var extra = chosen.Where(id => options.All(o => o.Id != id)).ToList();
            if (extra.Count > 0)
            {
                options.AddRange(await _db.ContentEntries.AsNoTracking().Where(e => extra.Contains(e.Id))
                    .Select(e => new EntryChoice(e.Id, e.Title, e.Published)).ToListAsync(ct));
            }

            relations[targetId] = options;
        }

        var media = HasMedia(fields) ? await MediaChoicesAsync(false, null, PickerSize, mediaIds) : Array.Empty<MediaChoice>();
        string? updatedBy = null;
        if (entry?.UpdatedById is { } by)
        {
            updatedBy = await _db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.Email).FirstOrDefaultAsync(ct);
        }

        return new EntryEditViewModel
        {
            Type = type,
            Fields = fields,
            Entry = entry,
            Values = values,
            Errors = (errors ?? Array.Empty<FieldError>()).GroupBy(e => e.Path).ToDictionary(g => g.Key, g => g.First().Message),
            BaseVersion = baseVersion ?? entry?.Version,
            Conflict = conflict,
            Relations = relations,
            RelationTypeNames = typeNames,
            Media = media,
            UpdatedBy = updatedBy,
            CanSave = entry is null ? Can(Area, PermissionActions.Create) : CanModify(Area, PermissionActions.Update, PermissionActions.UpdateOwn, entry.CreatedById),
            CanPublish = Can(Area, PermissionActions.Publish),
            CanDelete = entry is not null && type.Kind == ContentTypeKind.Collection && CanModify(Area, PermissionActions.Delete, PermissionActions.DeleteOwn, entry.CreatedById),
            CanCreate = type.Kind == ContentTypeKind.Collection && Can(Area, PermissionActions.Create),
            CanUpload = Can(PermissionAreas.Media, PermissionActions.Create),
        };
    }

    private static IEnumerable<Guid> AllRelationTargets(IReadOnlyList<FieldDefinition> fields) =>
        fields.SelectMany(f => (f.RelationTo is { } id ? new[] { id } : Array.Empty<Guid>()).Concat(AllRelationTargets(f.Fields)));

    private static bool HasMedia(IReadOnlyList<FieldDefinition> fields) =>
        fields.Any(f => f.Type is FieldTypes.Image or FieldTypes.File || HasMedia(f.Fields));
}

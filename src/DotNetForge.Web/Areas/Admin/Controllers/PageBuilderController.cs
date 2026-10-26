using System.Text.Json;
using DotNetForge.Content;
using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.PageBuilder;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// The page builder (.docs/pages/page-builder.md, .docs/features/page-builder.md): the full-screen editor of one page's
/// layout, its canvas document, and the JSON API the editor (<c>wwwroot/js/builder</c>) talks to. Who may open it:
/// whoever may edit the page (<c>Collection types</c> update, update.own on own pages, or a page edit grant);
/// publishing needs <c>publish</c>. Drafts are saved continuously; visitors see only what is published.
/// </summary>
[Route("admin/pages/{id:guid}/builder")]
public sealed class PageBuilderController : AdminControllerBase
{
    private const long MaxRequestBytes = 6 * 1024 * 1024;

    private readonly DotNetForgeDbContext _db;
    private readonly PageBuilderService _builder;
    private readonly MediaService _media;
    private readonly ThemeResolver _themes;
    private readonly ExtensionViewRenderer _renderer;
    private readonly IPublishedPages _published;
    private readonly AppEnvironment _env;
    private readonly ContentTypeService _types;
    private readonly ContentRenderService _content;
    private readonly ContentQueryService _query;

    public PageBuilderController(DotNetForgeDbContext db, PageBuilderService builder, MediaService media, ThemeResolver themes,
        ExtensionViewRenderer renderer, IPublishedPages published, AppEnvironment env, ContentTypeService types,
        ContentRenderService content, ContentQueryService query)
    {
        _types = types;
        _content = content;
        _query = query;
        _db = db;
        _builder = builder;
        _media = media;
        _themes = themes;
        _renderer = renderer;
        _published = published;
        _env = env;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        ViewData["CanUpload"] = Can(PermissionAreas.Media, PermissionActions.Create);
        return View(new BuilderViewModel(page.Id, page.Title, await PublicPathAsync(page), CanPublish));
    }

    /// <summary>
    /// The document inside the editor's iframe: the page as the site shows it - its theme's layout, header, menu and
    /// footer - with an empty mount where the layout goes, which the editor fills (PageLayoutViewComponent in canvas
    /// mode). So the canvas looks exactly like the live page around the part being edited.
    /// </summary>
    [HttpGet("canvas")]
    public async Task<IActionResult> Canvas(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        // Detached copy: makes theme views take their "this page has a layout" branch.
        page.LayoutJson ??= "{}";
        ViewData["BuilderCanvas"] = true;
        return await SitePageViewAsync(page);
    }

    /// <summary>The draft as visitors would see it once published (opened from the editor's Preview button).</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        // Detached copy: theme views render "the layout" - here the draft (PageLayoutViewComponent reads DraftPreview).
        page.LayoutJson = PageBuilderService.EditableJson(page);
        ViewData["DraftPreview"] = true;
        return await SitePageViewAsync(page);
    }

    /// <summary>The site's page view for <paramref name="page"/>: its theme's Page.cshtml (as HomeController renders it), else the built-in one.</summary>
    private async Task<IActionResult> SitePageViewAsync(Page page)
    {
        ViewData["AppName"] = _env.AppName;
        var theme = await _themes.GetActiveThemeAsync(HttpContext.RequestAborted, page.ThemeId);
        if (theme?.FindView("Page.cshtml") is not { } themeView)
        {
            return View("~/Views/Home/Page.cshtml", page);
        }

        ExtensionViewRenderer.AddExtensionViewData(ViewData, theme, admin: false);
        ViewData["Menu"] = (await _published.ListAsync(HttpContext.RequestAborted))
            .Where(p => p.DisplayInMenu && p.ParentPageId is null)
            .ToList();
        return View(_renderer.ToViewPath(themeView), page);
    }

    /// <summary>The draft's stylesheet, for the preview (never cached: the draft changes while editing).</summary>
    [HttpGet("draft.css")]
    public async Task<IActionResult> DraftCss(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        Response.Headers.CacheControl = "no-store";
        return PageBuilderService.Css(PageBuilderService.EditableJson(page)) is { } css ? Content(css, "text/css; charset=utf-8") : NotFound();
    }

    [HttpGet("state")]
    public async Task<IActionResult> State(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        var json = PageBuilderService.EditableJson(page);
        var savedBy = page.DraftUpdatedById is { } by
            ? await _db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.Email).FirstOrDefaultAsync()
            : null;
        var state = new JObject
        {
            ["page"] = new JObject
            {
                ["tabId"] = page.Id.ToString(),
                ["name"] = page.Title,
                ["url"] = await PublicPathAsync(page),
                ["enabled"] = page.LayoutJson is not null,
                ["published"] = page.LayoutJson is not null,
                ["publishedVersionId"] = null,
                ["publishedOn"] = Iso(page.LayoutPublishedDate),
                ["hostPane"] = string.Empty,
                ["panes"] = new JArray(),
            },
            ["draft"] = new JObject
            {
                ["document"] = json is null ? null : JToken.Parse(json),
                ["version"] = page.DraftVersion?.ToString(),
                ["savedOn"] = Iso(page.DraftUpdatedDate),
                ["savedBy"] = savedBy,
                ["pendingChanges"] = PageBuilderService.HasPendingChanges(page),
            },
            ["capabilities"] = new JObject
            {
                ["view"] = true,
                ["editContent"] = true,
                ["editLayout"] = true,
                ["publish"] = CanPublish,
                ["manage"] = CanPublish,
                ["admin"] = User.IsInRole(Roles.SuperAdmin),
            },
            ["modules"] = new JArray(),
            ["roles"] = new JArray(),
            ["others"] = new JArray(),
            ["user"] = new JObject { ["name"] = User.Identity?.Name },
            ["problems"] = new JArray(),
            ["canUpload"] = Can(PermissionAreas.Media, PermissionActions.Create),
        };
        return Json(state);
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveRequest request)
    {
        var (page, denied) = await LoadAsync(id, tracked: true);
        if (page is null)
        {
            return denied!;
        }

        var document = request.Document.ValueKind == JsonValueKind.Object ? JToken.Parse(request.Document.GetRawText()) : null;
        var result = await _builder.SaveDraftAsync(page, document, request.BaseVersion, request.Force, CurrentUserId, HttpContext.RequestAborted);
        if (result.Conflict is { } conflict)
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                message = $"{conflict.SavedBy ?? "Someone else"} saved this page in the meantime.",
                conflict = new { savedBy = conflict.SavedBy, savedOn = conflict.SavedOn, version = conflict.Version },
            });
        }

        return result.Error is not null
            ? BadRequest(new { message = result.Error })
            : Ok(new { version = result.Version, savedOn = result.SavedOn });
    }

    [HttpPost("publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id)
    {
        var (page, denied) = await LoadAsync(id, tracked: true);
        if (page is null)
        {
            return denied!;
        }

        if (!CanPublish)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You don't have permission to publish pages." });
        }

        var error = await _builder.PublishAsync(page, HttpContext.RequestAborted);
        return error is not null
            ? BadRequest(new { message = error })
            : Ok(new { versionId = (string?)null, publishedOn = page.LayoutPublishedDate, enabled = true });
    }

    [HttpPost("unpublish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        var (page, denied) = await LoadAsync(id, tracked: true);
        if (page is null)
        {
            return denied!;
        }

        if (!CanPublish)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You don't have permission to publish pages." });
        }

        await _builder.UnpublishAsync(page, HttpContext.RequestAborted);
        return Ok(new { ok = true });
    }

    [HttpPost("discard")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Discard(Guid id)
    {
        var (page, denied) = await LoadAsync(id, tracked: true);
        if (page is null)
        {
            return denied!;
        }

        await _builder.DiscardDraftAsync(page, CurrentUserId, HttpContext.RequestAborted);
        return Json(new JObject
        {
            ["version"] = page.DraftVersion?.ToString(),
            ["document"] = page.LayoutJson is null ? null : JToken.Parse(page.LayoutJson),
        });
    }

    /// <summary>The editor pings every 30 seconds; there is no presence tracking, so nobody else is reported.</summary>
    [HttpPost("heartbeat")]
    [ValidateAntiForgeryToken]
    public IActionResult Heartbeat() => Ok(new { others = Array.Empty<object>() });

    /// <summary>The public images of the media library, for the image picker.</summary>
    [HttpGet("media")]
    public async Task<IActionResult> Media(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        var files = await _db.MediaFiles.AsNoTracking()
            .Where(m => m.IsPublic && m.ContentType.StartsWith("image/"))
            .OrderByDescending(m => m.UploadedDate)
            .Take(200)
            .Select(m => new { m.Id, m.FileName, m.ContentType })
            .ToListAsync();
        return Ok(files.Select(f => new { url = MediaService.UrlFor(f.Id, f.FileName), name = f.FileName, contentType = f.ContentType }));
    }

    /// <summary>Uploads one image into the media library (public, so visitors can see it) and returns its URL.</summary>
    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MediaService.MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaService.MaxUploadBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid id, IFormFile? file)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        if (!Can(PermissionAreas.Media, PermissionActions.Create))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You don't have permission to upload files." });
        }

        if (file is null || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Choose an image file." });
        }

        var (saved, error) = await _media.UploadAsync(file, isPublic: true, CurrentUserId, HttpContext.RequestAborted);
        return saved is null
            ? BadRequest(new { message = error ?? "The upload failed." })
            : Ok(new { url = MediaService.UrlFor(saved), name = saved.FileName, contentType = saved.ContentType });
    }

    // ---- Content lists (.docs/features/structured-content.md#content-lists) -----------------------------------------

    /// <summary>
    /// The content types a content list can show: per type the fields a layout element can be bound to
    /// (<see cref="ItemData.Paths"/>), the fields a list can filter on and the ones it can sort by.
    /// </summary>
    [HttpGet("content/types")]
    public async Task<IActionResult> ContentTypes(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        var types = await _types.ListAsync(HttpContext.RequestAborted);
        var fieldsByType = types.ToDictionary(t => t.Id, t => (t.Name, Fields: ContentSchema.Parse(t.FieldsJson)));
        var counts = (await _db.ContentEntries.AsNoTracking().Where(e => e.Published)
                .Select(e => e.ContentTypeId).ToListAsync(HttpContext.RequestAborted))
            .GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var output = new JArray();
        foreach (var t in types)
        {
            var fields = fieldsByType[t.Id].Fields;
            var bindable = ItemData.Paths(fields, target => fieldsByType.TryGetValue(target, out var rel) ? (rel.Name, (IReadOnlyList<FieldDefinition>)rel.Fields) : null);
            var scalar = fields.Where(f => !FieldTypes.HasFields(f.Type) && f.Type != FieldTypes.RichText).ToList();
            output.Add(new JObject
            {
                ["id"] = t.Id.ToString(),
                ["name"] = t.Name,
                ["slug"] = t.Slug,
                ["kind"] = t.Kind.ToString().ToLowerInvariant(),
                ["icon"] = t.Icon,
                ["published"] = counts.GetValueOrDefault(t.Id),
                ["fields"] = new JArray(bindable.Select(b => new JObject { ["path"] = b.Path, ["label"] = b.Label, ["kind"] = b.Kind })),
                ["filters"] = new JArray(new[] { new JObject { ["key"] = ItemData.TitlePath, ["label"] = "Title", ["type"] = FieldTypes.Text } }
                    .Concat(scalar.Select(f => new JObject
                    {
                        ["key"] = f.Key,
                        ["label"] = f.Label,
                        ["type"] = f.Type,
                        ["choices"] = new JArray(f.Choices.Select(c => new JObject { ["value"] = c.Value, ["label"] = c.Label })),
                    }))),
                ["sorts"] = new JArray(new[]
                    {
                        new JObject { ["key"] = "", ["label"] = "Manual order" },
                        new JObject { ["key"] = "title", ["label"] = "Title" },
                        new JObject { ["key"] = "publishedAt", ["label"] = "Publish date" },
                        new JObject { ["key"] = "updatedAt", ["label"] = "Last updated" },
                    }
                    .Concat(scalar.Where(f => f.Type is not (FieldTypes.Image or FieldTypes.File or FieldTypes.Relationship or FieldTypes.Array))
                        .Select(f => new JObject { ["key"] = f.Key, ["label"] = f.Label }))),
            });
        }

        return Content(output.ToString(Formatting.None), "application/json");
    }

    /// <summary>The pages with a [param] segment, as item link patterns (<c>/products/[slug]</c> → <c>/products/{slug}</c>).</summary>
    [HttpGet("content/link-targets")]
    public async Task<IActionResult> LinkTargets(Guid id)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        var tree = await _db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.ParentPageId, p.Slug, p.Title }).ToListAsync(HttpContext.RequestAborted);
        var byId = tree.ToDictionary(p => p.Id);
        var output = new JArray();
        foreach (var p in tree.Where(p => p.Slug.StartsWith('[') && p.Slug.EndsWith(']')))
        {
            var segments = new List<string>();
            Guid? current = p.Id;
            for (var guard = 0; current is { } cid && byId.TryGetValue(cid, out var node) && guard < 64; guard++)
            {
                segments.Insert(0, node.Slug.StartsWith('[') && node.Slug.EndsWith(']') ? "{" + node.Slug.Trim('[', ']') + "}" : node.Slug.Trim('/'));
                current = node.ParentPageId;
            }

            var pattern = "/" + string.Join('/', segments.Where(s => s.Length > 0 && s != "home"));
            output.Add(new JObject { ["pattern"] = pattern, ["label"] = p.Title + " (" + pattern + ")", ["param"] = p.Slug.Trim('[', ']') });
        }

        return Content(output.ToString(Formatting.None), "application/json");
    }

    public sealed class PreviewRequest
    {
        public List<PreviewList> Lists { get; set; } = new();
    }

    public sealed class PreviewList
    {
        public string Id { get; set; } = string.Empty;
        public JsonElement Props { get; set; }
    }

    /// <summary>
    /// The entries the editor's content lists show: the same query visitors get (published entries), so the canvas
    /// previews real content. Props are cleaned by the page builder's validator first.
    /// </summary>
    [HttpPost("content/preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ContentPreview(Guid id, [FromBody] PreviewRequest request)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        var def = Schema.Component("collection")!;
        var lists = new List<(string Id, JObject Props)>();
        foreach (var list in request.Lists.Take(50))
        {
            if (Values.NodeId(new JValue(list.Id)) is not { } nodeId || list.Props.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var raw = (JObject)JToken.Parse(list.Props.GetRawText());
            var props = new JObject();
            foreach (var p in def.Props)
            {
                props[p.Key] = raw.Property(p.Key, StringComparison.Ordinal) is { } given ? DocumentValidator.NormalizeProp(p, given.Value) : p.CreateDefault();
            }

            lists.Add((nodeId, props));
        }

        var data = await _content.ResolveAsync(lists, null, _ => 1, preview: true, HttpContext.RequestAborted);
        return Content(data.Data.ToString(Formatting.None), "application/json");
    }

    /// <summary>Entries of a type for the "show one entry" picker: <c>[{ id, title, published }]</c>.</summary>
    [HttpGet("content/entries")]
    public async Task<IActionResult> ContentEntries(Guid id, Guid type, string? q)
    {
        var (page, denied) = await LoadAsync(id);
        if (page is null)
        {
            return denied!;
        }

        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFound();
        }

        var result = await _query.QueryAsync(new EntryQuery { Type = contentType, Search = q, Sort = "title", PageSize = 50 }, HttpContext.RequestAborted);
        return Ok(result.Items.Select(e => new { id = e.Id, title = ContentEntryService.Display(e), published = e.Published }));
    }

    private bool CanPublish => Can(PermissionAreas.CollectionTypes, PermissionActions.Publish);

    /// <summary>The page if the user may edit it; else the 404/403 result.</summary>
    private async Task<(Page? Page, IActionResult? Denied)> LoadAsync(Guid id, bool tracked = false)
    {
        var query = tracked ? _db.Pages : _db.Pages.AsNoTracking();
        var page = await query.FirstOrDefaultAsync(p => p.Id == id);
        if (page is null)
        {
            return (null, NotFound());
        }

        return await CanEditPageAsync(page) ? (page, null) : (null, Forbid());
    }

    /// <summary>The page's URL on the site (its slug path through the tree).</summary>
    private async Task<string> PublicPathAsync(Page page)
    {
        var tree = await _db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.ParentPageId, p.Slug })
            .ToDictionaryAsync(p => p.Id);
        var segments = new List<string>();
        Guid? current = page.Id;
        for (var guard = 0; current is { } cid && tree.TryGetValue(cid, out var node) && guard < 64; guard++)
        {
            segments.Insert(0, node.Slug.Trim('/'));
            current = node.ParentPageId;
        }

        var path = "/" + string.Join('/', segments.Where(s => s.Length > 0 && s != "home"));
        return path;
    }

    private static string? Iso(DateTime? value) => value?.ToString("O");

    private ContentResult Json(JObject value) => Content(value.ToString(Formatting.None), "application/json");

    public sealed class SaveRequest
    {
        public JsonElement Document { get; set; }
        public Guid? BaseVersion { get; set; }
        public bool Force { get; set; }
    }

    public sealed record BuilderViewModel(Guid PageId, string Title, string PublicPath, bool CanPublish);
}

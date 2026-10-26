using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Api.Authentication;
using DotNetForge.Api.Authorization;
using DotNetForge.Content;
using DotNetForge.Core.Content;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Api.Controllers;

/// <summary>
/// Structured content over the headless API (.docs/features/headless-api.md#structured-content): content types, and
/// entries with filters, sorting, paging and resolved media and relationships. Writes go through
/// <see cref="ContentEntryService"/> - the same validation, uniqueness and conflict rules as the Content Manager - and
/// are audited with the token as the actor. Reads return published values; clients with <c>entries.update</c> may ask
/// for the working copies (<c>?status=draft</c>).
/// </summary>
[Tags("Structured content")]
public sealed partial class EntriesApiController : ApiControllerBase
{
    private const int MaxPageSize = ContentQueryService.MaxPageSize;

    private readonly ContentTypeService _types;
    private readonly ContentEntryService _entries;
    private readonly ContentQueryService _query;
    private readonly ContentApiService _json;

    public EntriesApiController(ContentTypeService types, ContentEntryService entries, ContentQueryService query, ContentApiService json)
    {
        _types = types;
        _entries = entries;
        _query = query;
        _json = json;
    }

    [GeneratedRegex(@"^filter\[([$A-Za-z][$A-Za-z0-9_.]{0,119})\](?:\[([A-Za-z]+)\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex FilterKey();

    /// <summary>GET /api/content-types - every content type with its fields.</summary>
    [HttpGet("/api/content-types")]
    [RequireApiPermission(PermissionKeys.SchemaRead)]
    public async Task<IActionResult> GetTypes()
    {
        var types = await _types.ListAsync(HttpContext.RequestAborted);
        return Json(new JObject { ["data"] = new JArray(types.Select(ContentApiService.TypeJson)) });
    }

    /// <summary>GET /api/content-types/{type} - one content type by API id.</summary>
    [HttpGet("/api/content-types/{type}")]
    [RequireApiPermission(PermissionKeys.SchemaRead)]
    public async Task<IActionResult> GetType(string type)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        return contentType is null ? NotFoundJson(type) : Json(new JObject { ["data"] = ContentApiService.TypeJson(contentType) });
    }

    /// <summary>
    /// GET /api/entries/{type} - entries of a type. Query: <c>q</c> (search), <c>filter[field]=value</c> or
    /// <c>filter[field][op]=value</c> (op: eq, neq, contains, gt, gte, lt, lte, empty, notEmpty), <c>sort=field</c> or
    /// <c>sort=-field</c>, <c>page</c>, <c>pageSize</c> (≤ 100), <c>depth</c> (0 or 1), <c>status=draft</c> (working
    /// copies; needs <c>entries.update</c>). A single type answers with its one entry.
    /// </summary>
    [HttpGet("/api/entries/{type}")]
    [RequireApiPermission(PermissionKeys.EntriesRead)]
    public async Task<IActionResult> List(string type, string? q, string? sort, int page = 1, int pageSize = 20, int depth = 1, string? status = null)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFoundJson(type);
        }

        var drafts = status == "draft";
        if (drafts && !HasPermission(PermissionKeys.EntriesUpdate))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Missing required permission '{PermissionKeys.EntriesUpdate}' to read drafts." });
        }

        var filters = new List<EntryFilter>();
        foreach (var (key, value) in Request.Query)
        {
            var m = FilterKey().Match(key);
            if (!m.Success)
            {
                continue;
            }

            var op = m.Groups[2].Success ? m.Groups[2].Value : "eq";
            if (!ContentQueryService.Operators.Contains(op))
            {
                return BadRequest(new { error = $"Unknown filter operator '{op}'. Use one of: {string.Join(", ", ContentQueryService.Operators)}." });
            }

            filters.Add(new EntryFilter(m.Groups[1].Value, op, value.ToString()));
        }

        var descending = sort?.StartsWith('-') == true;
        var result = await _query.QueryAsync(new EntryQuery
        {
            Type = contentType,
            Published = !drafts,
            Search = q,
            Filters = filters,
            Sort = sort?.TrimStart('-') is { Length: > 0 } s ? s : null,
            Descending = descending,
            Page = page,
            PageSize = Math.Clamp(pageSize, 1, MaxPageSize),
        }, HttpContext.RequestAborted);
        var items = await _json.EntriesJsonAsync(contentType, result.Items, published: !drafts, Math.Clamp(depth, 0, 1), HttpContext.RequestAborted);
        if (contentType.Kind == ContentTypeKind.Single)
        {
            return items.Count == 0 ? NotFoundJson(type) : Json(new JObject { ["data"] = items[0] });
        }

        return Json(new JObject
        {
            ["data"] = items,
            ["meta"] = new JObject { ["total"] = result.Total, ["page"] = result.Page, ["pageSize"] = result.PageSize, ["pageCount"] = result.PageCount },
        });
    }

    /// <summary>GET /api/entries/{type}/{idOrSlug} - one published entry by id, or by the value of its slug field.</summary>
    [HttpGet("/api/entries/{type}/{idOrSlug}")]
    [RequireApiPermission(PermissionKeys.EntriesRead)]
    public async Task<IActionResult> Get(string type, string idOrSlug, int depth = 1, string? status = null)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFoundJson(type);
        }

        var drafts = status == "draft";
        if (drafts && !HasPermission(PermissionKeys.EntriesUpdate))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Missing required permission '{PermissionKeys.EntriesUpdate}' to read drafts." });
        }

        ContentEntry? entry;
        if (Guid.TryParse(idOrSlug, out var id))
        {
            entry = await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
            entry = entry is not null && (drafts || entry.Published) ? entry : null;
        }
        else
        {
            var slugField = ContentSchema.Parse(contentType.FieldsJson).FirstOrDefault(f => f.Type == FieldTypes.Slug)?.Key;
            entry = slugField is null ? null : await _query.FindPublishedByAsync(contentType, slugField, idOrSlug, HttpContext.RequestAborted);
        }

        if (entry is null)
        {
            return NotFound(new { error = $"No entry '{idOrSlug}' in '{type}'." });
        }

        var items = await _json.EntriesJsonAsync(contentType, new[] { entry }, published: !drafts, Math.Clamp(depth, 0, 1), HttpContext.RequestAborted);
        return Json(new JObject { ["data"] = items[0] });
    }

    /// <summary>The body of a write: the values (field key → value, references as ids) and whether to publish.</summary>
    public sealed class EntryWrite
    {
        public JsonElement Data { get; set; }

        public bool Publish { get; set; }

        /// <summary>Update only: the entry's <c>version</c> as read; a newer save in between is a 409.</summary>
        public Guid? Version { get; set; }
    }

    /// <summary>POST /api/entries/{type} - creates an entry (a draft, or published with <c>publish: true</c> and <c>entries.publish</c>).</summary>
    [HttpPost("/api/entries/{type}")]
    [RequireApiPermission(PermissionKeys.EntriesCreate)]
    public async Task<IActionResult> Create(string type, [FromBody] EntryWrite body)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        if (contentType is null)
        {
            return NotFoundJson(type);
        }

        if (contentType.Kind == ContentTypeKind.Single)
        {
            return BadRequest(new { error = $"'{type}' is a single type: update its entry with PUT /api/entries/{type}/{{id}}." });
        }

        return await SaveAsync(contentType, null, body, created: true);
    }

    /// <summary>PUT /api/entries/{type}/{id} - replaces an entry's values.</summary>
    [HttpPut("/api/entries/{type}/{id:guid}")]
    [RequireApiPermission(PermissionKeys.EntriesUpdate)]
    public async Task<IActionResult> Update(string type, Guid id, [FromBody] EntryWrite body)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        var entry = contentType is null ? null : await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
        if (contentType is null || entry is null)
        {
            return NotFound(new { error = $"No entry '{id}' in '{type}'." });
        }

        return await SaveAsync(contentType, entry, body, created: false);
    }

    /// <summary>POST /api/entries/{type}/{id}/publish - publishes the saved values.</summary>
    [HttpPost("/api/entries/{type}/{id:guid}/publish")]
    [RequireApiPermission(PermissionKeys.EntriesPublish)]
    public async Task<IActionResult> Publish(string type, Guid id)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        var entry = contentType is null ? null : await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
        if (contentType is null || entry is null)
        {
            return NotFound(new { error = $"No entry '{id}' in '{type}'." });
        }

        var errors = await _entries.PublishAsync(contentType, entry, null, HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            return BadRequest(new { error = "The entry can't be published.", errors = errors.Select(e => new { path = e.Path, message = e.Message }) });
        }

        return await EntryResultAsync(contentType, entry, StatusCodes.Status200OK);
    }

    /// <summary>POST /api/entries/{type}/{id}/unpublish - takes the entry off the site and the published API.</summary>
    [HttpPost("/api/entries/{type}/{id:guid}/unpublish")]
    [RequireApiPermission(PermissionKeys.EntriesPublish)]
    public async Task<IActionResult> Unpublish(string type, Guid id)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        var entry = contentType is null ? null : await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
        if (contentType is null || entry is null)
        {
            return NotFound(new { error = $"No entry '{id}' in '{type}'." });
        }

        await _entries.UnpublishAsync(contentType, entry, HttpContext.RequestAborted);
        return await EntryResultAsync(contentType, entry, StatusCodes.Status200OK);
    }

    /// <summary>DELETE /api/entries/{type}/{id}.</summary>
    [HttpDelete("/api/entries/{type}/{id:guid}")]
    [RequireApiPermission(PermissionKeys.EntriesDelete)]
    public async Task<IActionResult> Delete(string type, Guid id)
    {
        var contentType = await _types.FindAsync(type, HttpContext.RequestAborted);
        var entry = contentType is null ? null : await _entries.FindAsync(contentType, id, HttpContext.RequestAborted);
        if (contentType is null || entry is null)
        {
            return NotFound(new { error = $"No entry '{id}' in '{type}'." });
        }

        if (contentType.Kind == ContentTypeKind.Single)
        {
            return BadRequest(new { error = "The entry of a single type can't be deleted." });
        }

        await _entries.DeleteAsync(contentType, entry, HttpContext.RequestAborted);
        return NoContent();
    }

    private async Task<IActionResult> SaveAsync(ContentType type, ContentEntry? entry, EntryWrite body, bool created)
    {
        if (body.Data.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(new { error = "Send the values as an object: { \"data\": { ... } }." });
        }

        var publish = body.Publish || !type.Drafts;
        if (publish && !HasPermission(PermissionKeys.EntriesPublish))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = $"Missing required permission '{PermissionKeys.EntriesPublish}'." });
        }

        var input = (JObject)JToken.Parse(body.Data.GetRawText());
        var result = await _entries.SaveAsync(type, entry, input, publish, body.Version, null, HttpContext.RequestAborted);
        if (result.Conflict)
        {
            return Conflict(new { error = "The entry was changed since the version you sent.", version = entry!.Version });
        }

        if (!result.Saved)
        {
            return BadRequest(new { error = "The entry is not valid.", errors = result.Errors.Select(e => new { path = e.Path, message = e.Message }) });
        }

        return await EntryResultAsync(type, result.Entry!, created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private async Task<IActionResult> EntryResultAsync(ContentType type, ContentEntry entry, int status)
    {
        var json = await _json.EntriesJsonAsync(type, new[] { entry }, published: false, 0, HttpContext.RequestAborted);
        return new ContentResult
        {
            Content = new JObject { ["data"] = json[0] }.ToString(Formatting.None),
            ContentType = "application/json",
            StatusCode = status,
        };
    }

    /// <summary>Whether the token carries <paramref name="key"/> too (for what one action needs beyond its attribute).</summary>
    private bool HasPermission(string key) =>
        User.FindAll(ApiTokenDefaults.PermissionClaimType).Any(c => string.Equals(c.Value, key, StringComparison.OrdinalIgnoreCase));

    private NotFoundObjectResult NotFoundJson(string type) => NotFound(new { error = $"No content type '{type}'." });

    private ContentResult Json(JObject value) => Content(value.ToString(Formatting.None), "application/json");
}

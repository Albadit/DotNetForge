using System.Text.RegularExpressions;
using DotNetForge.Content;
using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.PageBuilder;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Web.Services;

/// <summary>
/// The entries a layout's content lists show, resolved before rendering (<see cref="Data"/> is the renderers'
/// <c>RenderContext.Data</c>). <see cref="MissingEntry"/>: a list shows the entry named in the URL
/// (<c>/products/[slug]</c>) and there is none - the page answers 404. <see cref="EntryTitle"/> is that entry's title,
/// for the page's <c>&lt;title&gt;</c>.
/// </summary>
public sealed record ContentRenderData(JObject Data, bool MissingEntry, string? EntryTitle)
{
    public static readonly ContentRenderData Empty = new(new JObject(), false, null);
}

/// <summary>
/// Reads what every content list of a layout needs (.docs/features/structured-content.md#content-lists): the list's
/// query (source type, one entry or many, filters, sort, page size, the page from <c>?pg-{nodeId}=n</c>), the published
/// entries, their media and linked entries - and turns each entry into item data (<see cref="ItemData"/>) the C# and
/// JavaScript renderers bind identically. Used for visitors, the editor's preview and the canvas. Queries run per
/// request, so an edited entry shows on every page at once; nothing here is cached.
/// </summary>
public sealed partial class ContentRenderService
{
    private readonly DotNetForgeDbContext _db;
    private readonly ContentQueryService _query;

    public ContentRenderService(DotNetForgeDbContext db, ContentQueryService query)
    {
        _db = db;
        _query = query;
    }

    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    /// <param name="routeValues">The values of the URL's dynamic segments (<c>[slug]</c> → <c>slug</c>).</param>
    /// <param name="pageOf">The page a paginated list shows, from the query string, by node id.</param>
    public Task<ContentRenderData> ResolveAsync(PageDocument document, IReadOnlyDictionary<string, string>? routeValues,
        Func<string, int> pageOf, CancellationToken ct = default) =>
        ResolveAsync(document.Walk().Select(document.Node).Where(n => n is { Type: "collection", Hidden: false }).Select(n => (n!.Id, n.Props)).ToList(),
            routeValues, pageOf, preview: false, ct);

    /// <summary>
    /// The entries of content lists given by node id and props. In a <paramref name="preview"/> (the editor's canvas)
    /// there is no URL, so a list showing the entry named in the URL shows the first one its query finds.
    /// </summary>
    public async Task<ContentRenderData> ResolveAsync(IReadOnlyList<(string Id, JObject Props)> lists,
        IReadOnlyDictionary<string, string>? routeValues, Func<string, int> pageOf, bool preview, CancellationToken ct = default)
    {
        if (lists.Count == 0)
        {
            return ContentRenderData.Empty;
        }

        var data = new JObject();
        var missing = false;
        string? entryTitle = null;
        var types = new Dictionary<Guid, ContentType?>();
        foreach (var node in lists)
        {
            var props = new ListProps(node.Props);
            if (!Guid.TryParse(props.Source, out var typeId))
            {
                continue;
            }

            if (!types.TryGetValue(typeId, out var type))
            {
                type = types[typeId] = await _db.ContentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == typeId, ct);
            }

            if (type is null)
            {
                continue;
            }

            IReadOnlyList<ContentEntry> entries;
            int total, page = 1, pageCount = 1;
            if (props.Mode == "single")
            {
                ContentEntry? entry;
                if (props.RouteParam.Length > 0 && !preview)
                {
                    var value = routeValues is not null && routeValues.TryGetValue(props.RouteParam, out var v) ? v : null;
                    entry = value is null ? null : await _query.FindPublishedByAsync(type, props.RouteField.Length > 0 ? props.RouteField : "slug", value, ct);
                    missing |= entry is null;
                    entryTitle ??= entry?.Title;
                }
                else if (Guid.TryParse(props.Entry, out var entryId))
                {
                    entry = await _db.ContentEntries.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == entryId && e.ContentTypeId == type.Id && e.Published, ct);
                }
                else
                {
                    entry = (await _query.QueryAsync(Query(type, props, 1, 1), ct)).Items.FirstOrDefault();
                }

                entries = entry is null ? Array.Empty<ContentEntry>() : new[] { entry };
                total = entries.Count;
            }
            else
            {
                var result = await _query.QueryAsync(Query(type, props, props.Paginate ? pageOf(node.Id) : 1, props.Limit), ct);
                entries = result.Items;
                total = result.Total;
                page = result.Page;
                pageCount = props.Paginate ? result.PageCount : 1;
            }

            data[node.Id] = new JObject
            {
                ["items"] = new JArray(await ItemsAsync(type, entries, props.ItemUrl, published: true, ct)),
                ["total"] = total,
                ["page"] = page,
                ["pageCount"] = pageCount,
            };
        }

        return new ContentRenderData(data, missing, entryTitle);
    }

    private static EntryQuery Query(ContentType type, ListProps props, int page, int pageSize) => new()
    {
        Type = type,
        Published = true,
        Filters = props.Filters,
        Sort = props.SortField.Length > 0 ? props.SortField : null,
        Descending = props.SortDir == "desc",
        Page = page,
        PageSize = pageSize,
    };

    /// <summary>
    /// The item data of entries (published values, or the working copies for the admin's previews): one query for
    /// their media, one for the entries they link to and one for those entries' types.
    /// </summary>
    public async Task<IReadOnlyList<JObject>> ItemsAsync(ContentType type, IReadOnlyList<ContentEntry> entries, string itemUrl, bool published, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<JObject>();
        }

        var fields = ContentSchema.Parse(type.FieldsJson);
        var values = entries.ToDictionary(e => e.Id, e => ContentEntryService.Parse(published ? e.PublishedDataJson : e.DataJson));

        // Linked entries (published only, as visitors see them) and their types.
        var relatedIds = new HashSet<Guid>();
        foreach (var data in values.Values)
        {
            foreach (var (id, _) in EntryData.References(fields, data).Entries)
            {
                relatedIds.Add(id);
            }
        }

        var related = new Dictionary<Guid, RelatedEntry>();
        if (relatedIds.Count > 0)
        {
            var ids = relatedIds.ToList();
            var rows = await _db.ContentEntries.AsNoTracking()
                .Where(e => ids.Contains(e.Id) && e.Published)
                .Select(e => new { e.Id, e.Title, e.ContentTypeId, e.PublishedDataJson })
                .ToListAsync(ct);
            var typeIds = rows.Select(r => r.ContentTypeId).Distinct().ToList();
            var relatedFields = (await _db.ContentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.FieldsJson }).ToListAsync(ct))
                .ToDictionary(t => t.Id, t => ContentSchema.Parse(t.FieldsJson));
            foreach (var r in rows)
            {
                related[r.Id] = new RelatedEntry(r.Id, r.Title, relatedFields.GetValueOrDefault(r.ContentTypeId) ?? Array.Empty<FieldDefinition>(),
                    ContentEntryService.Parse(r.PublishedDataJson));
            }
        }

        // Media of the entries and of the linked entries. Only public files: a page is public.
        var mediaIds = new HashSet<Guid>();
        foreach (var data in values.Values)
        {
            mediaIds.UnionWith(EntryData.References(fields, data).Media);
        }

        foreach (var r in related.Values)
        {
            mediaIds.UnionWith(EntryData.References(r.Fields, r.Data).Media);
        }

        var media = new Dictionary<Guid, MediaInfo>();
        if (mediaIds.Count > 0)
        {
            var ids = mediaIds.ToList();
            foreach (var m in await _db.MediaFiles.AsNoTracking().Where(m => ids.Contains(m.Id) && m.IsPublic)
                         .Select(m => new { m.Id, m.FileName, m.OriginalName, m.ContentType }).ToListAsync(ct))
            {
                media[m.Id] = new MediaInfo(MediaService.UrlFor(m.Id, m.FileName), string.IsNullOrEmpty(m.OriginalName) ? m.FileName : m.OriginalName,
                    m.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
            }
        }

        var items = new List<JObject>();
        foreach (var entry in entries)
        {
            var item = ItemData.Build(entry.Id, entry.Title, string.Empty, fields, values[entry.Id], media.GetValueOrDefault, related.GetValueOrDefault);
            var url = ItemUrl(itemUrl, (JObject)item["fields"]!);
            item["fields"]![ItemData.UrlPath] = new JObject { ["text"] = url, ["url"] = url };
            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// An entry's link from a list's pattern (<c>/products/{slug}</c>): each placeholder is the field's text,
    /// URL-encoded. Empty when there is no pattern or the result is not a safe link.
    /// </summary>
    public static string ItemUrl(string pattern, JObject fields)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        var missing = false;
        var url = Placeholder().Replace(pattern.Trim(), m =>
        {
            var text = (string?)(fields.Property(m.Groups[1].Value, StringComparison.Ordinal)?.Value as JObject)?["text"] ?? string.Empty;
            missing |= text.Length == 0;
            return Uri.EscapeDataString(text);
        });

        // An entry without the value (no slug yet) gets no link rather than one to the wrong page.
        return missing ? string.Empty : Values.LinkUrl(url) ?? string.Empty;
    }

    /// <summary>The props of a content list node, read defensively (stored documents are normalized, but not trusted).</summary>
    private sealed class ListProps
    {
        public ListProps(JObject p)
        {
            Source = Str(p["source"]);
            Mode = Str(p["mode"]);
            Entry = Str(p["entry"]);
            RouteParam = Str(p["routeParam"]);
            RouteField = Str(p["routeField"]);
            SortField = Str(p["sortField"]);
            SortDir = Str(p["sortDir"]);
            ItemUrl = Str(p["itemUrl"]);
            Limit = p["limit"] is { Type: JTokenType.Integer } l ? Math.Clamp((int)l, 1, ContentQueryService.MaxPageSize) : 6;
            Paginate = p["paginate"] is { Type: JTokenType.Boolean } b && (bool)b;
            Filters = (p["filters"] as JArray ?? new JArray()).OfType<JObject>()
                .Select(f => new EntryFilter(Str(f["field"]), Str(f["op"]), Str(f["value"])))
                .Where(f => f.Field.Length > 0)
                .ToList();
        }

        public string Source { get; }

        public string Mode { get; }

        public string Entry { get; }

        public string RouteParam { get; }

        public string RouteField { get; }

        public string SortField { get; }

        public string SortDir { get; }

        public string ItemUrl { get; }

        public int Limit { get; }

        public bool Paginate { get; }

        public IReadOnlyList<EntryFilter> Filters { get; }

        private static string Str(JToken? t) => t is { Type: JTokenType.String } ? (string)t! : string.Empty;
    }
}

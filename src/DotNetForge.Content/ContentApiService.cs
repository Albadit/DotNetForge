using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Media;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Content;

/// <summary>
/// How content types and entries look in the headless API (.docs/features/headless-api.md#structured-content): a type
/// with its field definitions; an entry with its status, dates and values. With <c>depth</c> 1 (the default) media
/// references become <c>{ id, url, name, contentType }</c> and relationships <c>{ id, title, type, data }</c> (the linked
/// entry's own values, not expanded further); with 0 they stay ids, as they are written.
/// </summary>
public sealed class ContentApiService
{
    private readonly DotNetForgeDbContext _db;

    public ContentApiService(DotNetForgeDbContext db)
    {
        _db = db;
    }

    public static JObject TypeJson(ContentType t) => new()
    {
        ["id"] = t.Id.ToString(),
        ["name"] = t.Name,
        ["slug"] = t.Slug,
        ["kind"] = t.Kind.ToString().ToLowerInvariant(),
        ["description"] = t.Description,
        ["titleField"] = ContentSchema.TitleKey(ContentSchema.Parse(t.FieldsJson), t.TitleField),
        ["drafts"] = t.Drafts,
        ["fields"] = JArray.Parse(t.FieldsJson),
        ["updatedAt"] = t.UpdatedDate,
    };

    /// <summary>
    /// Entries as the API returns them: <paramref name="published"/> selects the published values (else the working
    /// copies, for clients that may update).
    /// </summary>
    public async Task<JArray> EntriesJsonAsync(ContentType type, IReadOnlyList<ContentEntry> entries, bool published, int depth, CancellationToken ct = default)
    {
        var fields = ContentSchema.Parse(type.FieldsJson);
        var values = entries.Select(e => ContentEntryService.Parse(published ? e.PublishedDataJson ?? e.DataJson : e.DataJson)).ToList();
        IReadOnlyDictionary<Guid, JObject> media = new Dictionary<Guid, JObject>();
        IReadOnlyDictionary<Guid, JObject> related = new Dictionary<Guid, JObject>();
        if (depth > 0 && entries.Count > 0)
        {
            var mediaIds = new HashSet<Guid>();
            var entryIds = new HashSet<Guid>();
            foreach (var v in values)
            {
                var refs = EntryData.References(fields, v);
                mediaIds.UnionWith(refs.Media);
                entryIds.UnionWith(refs.Entries.Select(r => r.Entry));
            }

            media = await MediaAsync(mediaIds, ct);
            related = await RelatedAsync(entryIds, published, ct);
        }

        var output = new JArray();
        for (var i = 0; i < entries.Count; i++)
        {
            output.Add(EntryJson(type, entries[i], depth > 0 ? Expand(fields, values[i], media, related) : values[i]));
        }

        return output;
    }

    private static JObject EntryJson(ContentType type, ContentEntry e, JObject data) => new()
    {
        ["id"] = e.Id.ToString(),
        ["type"] = type.Slug,
        ["title"] = e.Title,
        ["status"] = !e.Published ? "draft" : e.HasDraft ? "changed" : "published",
        ["version"] = e.Version.ToString(),
        ["sortOrder"] = e.SortOrder,
        ["createdAt"] = e.CreatedDate,
        ["updatedAt"] = e.UpdatedDate,
        ["publishedAt"] = e.PublishedDate,
        ["data"] = data,
    };

    private static JObject Expand(IReadOnlyList<FieldDefinition> fields, JObject data, IReadOnlyDictionary<Guid, JObject> media, IReadOnlyDictionary<Guid, JObject> related)
    {
        var output = new JObject();
        foreach (var f in fields)
        {
            if (data[f.Key] is not { } value)
            {
                continue;
            }

            output[f.Key] = f.Type switch
            {
                FieldTypes.Image or FieldTypes.File => Resolve(value, media),
                FieldTypes.Relationship => Resolve(value, related),
                FieldTypes.Group when value is JObject o => Expand(f.Fields, o, media, related),
                FieldTypes.Repeater when value is JArray rows => new JArray(rows.OfType<JObject>().Select(r => Expand(f.Fields, r, media, related))),
                _ => value.DeepClone(),
            };
        }

        return output;
    }

    /// <summary>One id (or a list) replaced by the referenced objects; missing ones are dropped (a single one becomes null).</summary>
    private static JToken Resolve(JToken value, IReadOnlyDictionary<Guid, JObject> found)
    {
        JToken? One(string? id) => Guid.TryParse(id, out var g) && found.TryGetValue(g, out var o) ? o.DeepClone() : null;
        return value is JArray list
            ? new JArray(list.Select(v => One((string?)v)).Where(v => v is not null))
            : One((string?)value) ?? JValue.CreateNull();
    }

    private async Task<IReadOnlyDictionary<Guid, JObject>> MediaAsync(HashSet<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, JObject>();
        }

        var list = ids.ToList();
        return (await _db.MediaFiles.AsNoTracking().Where(m => list.Contains(m.Id))
                .Select(m => new { m.Id, m.FileName, m.OriginalName, m.ContentType, m.IsPublic }).ToListAsync(ct))
            .ToDictionary(m => m.Id, m => new JObject
            {
                ["id"] = m.Id.ToString(),
                ["url"] = MediaUrls.For(m.Id, m.FileName),
                ["name"] = string.IsNullOrEmpty(m.OriginalName) ? m.FileName : m.OriginalName,
                ["contentType"] = m.ContentType,
                ["public"] = m.IsPublic,
            });
    }

    private async Task<IReadOnlyDictionary<Guid, JObject>> RelatedAsync(HashSet<Guid> ids, bool published, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, JObject>();
        }

        var list = ids.ToList();
        var rows = await _db.ContentEntries.AsNoTracking()
            .Where(e => list.Contains(e.Id) && (!published || e.Published))
            .Select(e => new { e.Id, e.Title, e.ContentTypeId, e.DataJson, e.PublishedDataJson })
            .ToListAsync(ct);
        var typeIds = rows.Select(r => r.ContentTypeId).Distinct().ToList();
        var slugs = await _db.ContentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Slug }).ToDictionaryAsync(t => t.Id, t => t.Slug, ct);
        return rows.ToDictionary(r => r.Id, r => new JObject
        {
            ["id"] = r.Id.ToString(),
            ["title"] = r.Title,
            ["type"] = slugs.GetValueOrDefault(r.ContentTypeId),
            ["data"] = ContentEntryService.Parse(published ? r.PublishedDataJson : r.DataJson),
        });
    }
}

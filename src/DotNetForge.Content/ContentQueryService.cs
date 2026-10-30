using System.Globalization;
using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Content;

/// <summary>One condition on a field: <c>eq</c>, <c>neq</c>, <c>contains</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>, <c>empty</c>, <c>notEmpty</c>.</summary>
public sealed record EntryFilter(string Field, string Op, string? Value);

/// <summary>
/// What to list. <see cref="Published"/> selects the published values (site, API) or the working copies (admin).
/// <see cref="Sort"/> is a built-in column (<c>sortOrder</c>, <c>title</c>, <c>createdAt</c>, <c>updatedAt</c>,
/// <c>publishedAt</c>) or a field key.
/// </summary>
public sealed record EntryQuery
{
    public required ContentType Type { get; init; }

    public bool Published { get; init; }

    public string? Search { get; init; }

    /// <summary>Admin only: <c>draft</c> (never published), <c>published</c> (no pending changes), <c>changed</c>.</summary>
    public string? Status { get; init; }

    public IReadOnlyList<EntryFilter> Filters { get; init; } = Array.Empty<EntryFilter>();

    public string? Sort { get; init; }

    public bool Descending { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

/// <summary>One page of entries and where it is in the whole list.</summary>
public sealed record EntryPage(IReadOnlyList<ContentEntry> Items, int Total, int Page, int PageSize)
{
    public int PageCount => Total == 0 ? 1 : (Total + PageSize - 1) / PageSize;
}

/// <summary>
/// Lists entries with search, filters, sorting and paging (.docs/features/structured-content.md#querying) - for the
/// Content Manager, content blocks on pages and the API alike. Every query reads one table: the entry columns, then
/// the value index (<see cref="ContentEntryValue"/>) per filtered or sorted field, combined in memory, then the one page
/// of entries by id.
/// </summary>
public sealed class ContentQueryService
{
    public const int MaxPageSize = 100;

    public static readonly IReadOnlyList<string> Operators = new[] { "eq", "neq", "contains", "gt", "gte", "lt", "lte", "empty", "notEmpty" };

    private readonly DotNetForgeDbContext _db;

    public ContentQueryService(DotNetForgeDbContext db)
    {
        _db = db;
    }

    public async Task<EntryPage> QueryAsync(EntryQuery query, CancellationToken ct = default)
    {
        var type = query.Type;
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var rows = _db.ContentEntries.AsNoTracking().Where(e => e.ContentTypeId == type.Id);
        if (query.Published)
        {
            rows = rows.Where(e => e.Published);
        }

        rows = query.Status switch
        {
            "draft" => rows.Where(e => !e.Published),
            "published" => rows.Where(e => e.Published && !e.HasDraft),
            "changed" => rows.Where(e => e.Published && e.HasDraft),
            _ => rows,
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(e => e.SearchText.Contains(term));
        }

        var candidates = await rows
            .Select(e => new Row(e.Id, e.SortOrder, e.Title, e.CreatedDate, e.UpdatedDate, e.PublishedDate))
            .ToListAsync(ct);

        var fields = ContentSchema.Parse(type.FieldsJson);
        foreach (var filter in query.Filters)
        {
            if (candidates.Count == 0)
            {
                break;
            }

            var keep = await MatchAsync(type, fields, filter, query.Published, candidates, ct);
            candidates = candidates.Where(c => keep.Contains(c.Id)).ToList();
        }

        var ordered = await SortAsync(type, fields, query, candidates, ct);
        var total = ordered.Count;
        var pageCount = total == 0 ? 1 : (total + pageSize - 1) / pageSize;
        var page = Math.Clamp(query.Page, 1, pageCount);
        var ids = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.Id).ToList();
        var entries = ids.Count == 0
            ? new List<ContentEntry>()
            : await _db.ContentEntries.AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        var byId = entries.ToDictionary(e => e.Id);
        return new EntryPage(ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), total, page, pageSize);
    }

    /// <summary>The published entry whose <paramref name="field"/> equals <paramref name="value"/> (a detail page's URL value), or null.</summary>
    public async Task<ContentEntry?> FindPublishedByAsync(ContentType type, string field, string value, CancellationToken ct = default)
    {
        Guid? id;
        if (field == ItemData.IdPath)
        {
            id = Guid.TryParse(value, out var g) ? g : null;
        }
        else
        {
            var text = EntryData.IndexText(value);
            var match = await _db.ContentEntryValues.AsNoTracking()
                .Where(v => v.ContentTypeId == type.Id && v.Published && v.Field == field && v.Text == text)
                .Select(v => new { v.EntryId }).FirstOrDefaultAsync(ct);
            id = match?.EntryId;
        }

        return id is null
            ? null
            : await _db.ContentEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.ContentTypeId == type.Id && e.Published, ct);
    }

    private async Task<HashSet<Guid>> MatchAsync(ContentType type, IReadOnlyList<FieldDefinition> fields, EntryFilter filter, bool published,
        IReadOnlyList<Row> candidates, CancellationToken ct)
    {
        var all = candidates.Select(c => c.Id).ToHashSet();
        var value = filter.Value?.Trim() ?? string.Empty;
        if (filter.Field is ItemData.IdPath)
        {
            return Guid.TryParse(value, out var id) ? all.Where(c => (c == id) == (filter.Op != "neq")).ToHashSet() : new HashSet<Guid>();
        }

        if (filter.Field is ItemData.TitlePath)
        {
            var title = value.ToLowerInvariant();
            return candidates.Where(c => Compare(c.Title.ToLowerInvariant(), null, null, filter.Op, title)).Select(c => c.Id).ToHashSet();
        }

        var field = fields.FirstOrDefault(f => f.Key == filter.Field);
        if (field is null)
        {
            // An unknown field (removed from the type) matches nothing, so a stale filter never shows everything.
            return new HashSet<Guid>();
        }

        var values = await _db.ContentEntryValues.AsNoTracking()
            .Where(v => v.ContentTypeId == type.Id && v.Published == published && v.Field == field.Key)
            .Select(v => new { v.EntryId, v.Text, v.Number, v.Date })
            .ToListAsync(ct);
        var withValue = values.Select(v => v.EntryId).ToHashSet();
        switch (filter.Op)
        {
            case "empty":
                return all.Where(id => !withValue.Contains(id)).ToHashSet();
            case "notEmpty":
                return all.Where(withValue.Contains).ToHashSet();
            case "neq":
                {
                    var equal = values.Where(v => Compare(v.Text, v.Number, v.Date, "eq", value)).Select(v => v.EntryId).ToHashSet();
                    return all.Where(id => !equal.Contains(id)).ToHashSet();
                }

            default:
                return values.Where(v => Compare(v.Text, v.Number, v.Date, filter.Op, value)).Select(v => v.EntryId).ToHashSet();
        }
    }

    /// <summary>One indexed value against a filter value: numbers and dates compare as such, everything else as lower-case text.</summary>
    private static bool Compare(string? text, double? number, DateTime? date, string op, string value)
    {
        int? order = null;
        if (number is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            order = number.Value.CompareTo(n);
        }
        else if (date is not null && EntryData.TryDate(value, out var d))
        {
            order = date.Value.CompareTo(d);
        }

        var t = text ?? string.Empty;
        var v = value.ToLowerInvariant();
        return op switch
        {
            "eq" => order is not null ? order == 0 : t == v,
            "contains" => t.Contains(v, StringComparison.Ordinal),
            "gt" => (order ?? string.CompareOrdinal(t, v)) > 0,
            "gte" => (order ?? string.CompareOrdinal(t, v)) >= 0,
            "lt" => (order ?? string.CompareOrdinal(t, v)) < 0,
            "lte" => (order ?? string.CompareOrdinal(t, v)) <= 0,
            _ => false,
        };
    }

    private async Task<List<Row>> SortAsync(ContentType type, IReadOnlyList<FieldDefinition> fields, EntryQuery query, List<Row> rows, CancellationToken ct)
    {
        IOrderedEnumerable<Row> ordered;
        switch (query.Sort)
        {
            case null or "" or "sortOrder":
                ordered = query.Descending ? rows.OrderByDescending(r => r.SortOrder) : rows.OrderBy(r => r.SortOrder);
                break;
            case "title" or ItemData.TitlePath:
                ordered = query.Descending ? rows.OrderByDescending(r => r.Title, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase);
                break;
            case "createdAt":
                ordered = query.Descending ? rows.OrderByDescending(r => r.Created) : rows.OrderBy(r => r.Created);
                break;
            case "updatedAt":
                ordered = query.Descending ? rows.OrderByDescending(r => r.Updated) : rows.OrderBy(r => r.Updated);
                break;
            case "publishedAt":
                // Never-published entries last either way.
                ordered = query.Descending
                    ? rows.OrderBy(r => r.Published is null).ThenByDescending(r => r.Published)
                    : rows.OrderBy(r => r.Published is null).ThenBy(r => r.Published);
                break;
            default:
                {
                    var field = fields.FirstOrDefault(f => f.Key == query.Sort);
                    if (field is null)
                    {
                        ordered = rows.OrderBy(r => r.SortOrder);
                        break;
                    }

                    // The first value of each entry (a multi-value field sorts by its first value); entries without one go last.
                    var keys = (await _db.ContentEntryValues.AsNoTracking()
                            .Where(v => v.ContentTypeId == type.Id && v.Published == query.Published && v.Field == field.Key)
                            .Select(v => new { v.EntryId, v.Text, v.Number, v.Date })
                            .ToListAsync(ct))
                        .GroupBy(v => v.EntryId)
                        .ToDictionary(g => g.Key, g => g.First());
                    ordered = rows.OrderBy(r => !keys.ContainsKey(r.Id));
                    ordered = query.Descending
                        ? ordered.ThenByDescending(r => keys.GetValueOrDefault(r.Id)?.Number).ThenByDescending(r => keys.GetValueOrDefault(r.Id)?.Date).ThenByDescending(r => keys.GetValueOrDefault(r.Id)?.Text, StringComparer.Ordinal)
                        : ordered.ThenBy(r => keys.GetValueOrDefault(r.Id)?.Number).ThenBy(r => keys.GetValueOrDefault(r.Id)?.Date).ThenBy(r => keys.GetValueOrDefault(r.Id)?.Text, StringComparer.Ordinal);
                    break;
                }
        }

        return ordered.ThenBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();
    }

    private sealed record Row(Guid Id, int SortOrder, string Title, DateTime Created, DateTime Updated, DateTime? Published);
}

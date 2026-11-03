using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Content;

/// <summary>The outcome of saving an entry: the entry, or the problems (by field path), or a newer version saved in between.</summary>
public sealed record EntrySaveResult(ContentEntry? Entry, IReadOnlyList<FieldError> Errors, bool Conflict)
{
    public bool Saved => Entry is not null && Errors.Count == 0 && !Conflict;
}

/// <summary>
/// The rules for entries (.docs/features/structured-content.md#entries): saving the working copy, publishing,
/// unpublishing, duplicating and deleting, and keeping the derived columns and the value index
/// (<see cref="ContentEntryValue"/>) in step with the JSON. Saving validates against the type's fields
/// (<see cref="EntryData"/>), checks unique fields and refuses a save based on an older version. Every method that
/// changes something saves and audits.
/// </summary>
public sealed class ContentEntryService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;

    public ContentEntryService(DotNetForgeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<ContentEntry?> FindAsync(ContentType type, Guid id, CancellationToken ct = default) =>
        _db.ContentEntries.FirstOrDefaultAsync(e => e.ContentTypeId == type.Id && e.Id == id, ct);

    /// <summary>The one entry of a single type, or null when it has none yet (its first save creates it).</summary>
    public Task<ContentEntry?> SingleEntryAsync(ContentType type, CancellationToken ct = default) =>
        _db.ContentEntries.Where(e => e.ContentTypeId == type.Id)
            .OrderBy(e => e.CreatedDate).FirstOrDefaultAsync(ct);

    /// <summary>Adds (tracked, not saved) the empty entry a single type starts with.</summary>
    public ContentEntry AddSingleEntry(ContentType type, Guid? userId)
    {
        var fields = ContentSchema.Parse(type.FieldsJson);
        var data = EntryData.Defaults(fields);
        var entry = NewEntry(type, fields, data, userId, 0);
        _db.ContentEntries.Add(entry);
        AddValues(entry, fields, data, published: false);
        return entry;
    }

    /// <summary>
    /// Saves an entry's values (creates the entry when <paramref name="entry"/> is null). With <paramref name="publish"/>
    /// - or for a type without drafts - the values are published too, which also requires the required fields.
    /// </summary>
    public async Task<EntrySaveResult> SaveAsync(ContentType type, ContentEntry? entry, JObject input, bool publish, Guid? baseVersion,
        Guid? userId, CancellationToken ct = default)
    {
        publish = publish || !type.Drafts;
        if (entry is not null && baseVersion is { } loaded && loaded != entry.Version)
        {
            return new EntrySaveResult(entry, Array.Empty<FieldError>(), Conflict: true);
        }

        var fields = ContentSchema.Parse(type.FieldsJson);
        var validation = await ValidateAsync(type, fields, input, publish, ct);
        var errors = validation.Errors.ToList();
        errors.AddRange(await UniqueErrorsAsync(type, fields, validation.Data, entry?.Id, ct));
        if (errors.Count > 0)
        {
            return new EntrySaveResult(entry, errors, Conflict: false);
        }

        var created = entry is null;
        if (entry is null)
        {
            // The last position.
            var last = await _db.ContentEntries.Where(e => e.ContentTypeId == type.Id)
                .OrderByDescending(e => e.SortOrder).Select(e => new { e.SortOrder }).FirstOrDefaultAsync(ct);
            entry = NewEntry(type, fields, validation.Data, userId, (last?.SortOrder ?? -1) + 1);
            _db.ContentEntries.Add(entry);
        }
        else
        {
            await RemoveValuesAsync(entry.Id, published: false, ct);
        }

        var json = validation.Data.ToString(Formatting.None);
        Apply(entry, type, fields, json, validation.Data, userId);
        AddValues(entry, fields, validation.Data, published: false);
        if (publish)
        {
            await PublishDataAsync(entry, fields, json, validation.Data, userId, ct);
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(created ? AuditActions.EntryCreated : AuditActions.EntryUpdated, AuditEntity, entry.Id.ToString(), AuditDisplay(type, entry), cancellationToken: ct);
        if (publish)
        {
            await _audit.LogAsync(AuditActions.EntryPublished, AuditEntity, entry.Id.ToString(), AuditDisplay(type, entry), cancellationToken: ct);
        }

        return new EntrySaveResult(entry, Array.Empty<FieldError>(), Conflict: false);
    }

    /// <summary>Publishes the entry's saved values. Returns the problems that keep it from being published (required fields...).</summary>
    public async Task<IReadOnlyList<FieldError>> PublishAsync(ContentType type, ContentEntry entry, Guid? userId, CancellationToken ct = default)
    {
        var fields = ContentSchema.Parse(type.FieldsJson);
        var validation = await ValidateAsync(type, fields, Parse(entry.DataJson), publish: true, ct);
        var errors = validation.Errors.ToList();
        errors.AddRange(await UniqueErrorsAsync(type, fields, validation.Data, entry.Id, ct));
        if (errors.Count > 0)
        {
            return errors;
        }

        await PublishDataAsync(entry, fields, validation.Data.ToString(Formatting.None), validation.Data, userId, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EntryPublished, AuditEntity, entry.Id.ToString(), AuditDisplay(type, entry), cancellationToken: ct);
        return errors;
    }

    /// <summary>Takes the entry off the site and the API; its values stay as the working copy.</summary>
    public async Task UnpublishAsync(ContentType type, ContentEntry entry, CancellationToken ct = default)
    {
        entry.PublishedDataJson = null;
        entry.Published = false;
        entry.PublishedDate = null;
        entry.PublishedById = null;
        entry.HasDraft = true;
        await RemoveValuesAsync(entry.Id, published: true, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EntryUnpublished, AuditEntity, entry.Id.ToString(), AuditDisplay(type, entry), cancellationToken: ct);
    }

    /// <summary>
    /// Copies an entry (unpublished, placed right after the original). Unique values are made unique again: slugs
    /// get <c>-copy</c>, other unique fields are left empty.
    /// </summary>
    public async Task<ContentEntry> DuplicateAsync(ContentType type, ContentEntry source, Guid? userId, CancellationToken ct = default)
    {
        var fields = ContentSchema.Parse(type.FieldsJson);
        var data = Parse(source.DataJson);
        var titleKey = ContentSchema.TitleKey(fields, type.TitleField);
        foreach (var f in fields.Where(f => f.Unique))
        {
            if (f.Type == FieldTypes.Slug && data[f.Key] is JValue { Type: JTokenType.String } slug)
            {
                data[f.Key] = await FreeValueAsync(type, f.Key, (string)slug! + "-copy", ct);
            }
            else if (f.Key != titleKey)
            {
                data.Remove(f.Key);
            }
        }

        if (titleKey is not null && data[titleKey] is JValue { Type: JTokenType.String } title)
        {
            var copy = (string)title! + " (copy)";
            data[titleKey] = fields.First(f => f.Key == titleKey).Unique ? await FreeValueAsync(type, titleKey, copy, ct) : copy;
        }

        // Later entries move down one place so the copy sits right below the original.
        var later = await _db.ContentEntries.Where(e => e.ContentTypeId == type.Id && e.SortOrder > source.SortOrder).ToListAsync(ct);
        foreach (var e in later)
        {
            e.SortOrder++;
        }

        var entry = NewEntry(type, fields, data, userId, source.SortOrder + 1);
        _db.ContentEntries.Add(entry);
        AddValues(entry, fields, data, published: false);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EntryCreated, AuditEntity, entry.Id.ToString(), AuditDisplay(type, entry), details: $"Duplicate of '{Display(source)}'", cancellationToken: ct);
        return entry;
    }

    public async Task DeleteAsync(ContentType type, ContentEntry entry, CancellationToken ct = default)
    {
        await RemoveValuesAsync(entry.Id, published: null, ct);
        _db.ContentEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EntryDeleted, AuditEntity, entry.Id.ToString(), AuditDisplay(type, entry), cancellationToken: ct);
    }

    /// <summary>Deletes several entries of a type (bulk delete). Returns how many were deleted.</summary>
    public async Task<int> DeleteManyAsync(ContentType type, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var entries = await _db.ContentEntries.Where(e => e.ContentTypeId == type.Id && ids.Contains(e.Id)).ToListAsync(ct);
        var entryIds = entries.Select(e => e.Id).ToList();
        _db.ContentEntryValues.RemoveRange(await _db.ContentEntryValues.Where(v => entryIds.Contains(v.EntryId)).ToListAsync(ct));
        _db.ContentEntries.RemoveRange(entries);
        await _db.SaveChangesAsync(ct);
        foreach (var e in entries)
        {
            await _audit.LogAsync(AuditActions.EntryDeleted, AuditEntity, e.Id.ToString(), AuditDisplay(type, e), cancellationToken: ct);
        }

        return entries.Count;
    }

    /// <summary>Stores a new manual order (drag and drop in the entry list); ids not in the type are ignored.</summary>
    public async Task ReorderAsync(ContentType type, IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var entries = await _db.ContentEntries.Where(e => e.ContentTypeId == type.Id && ids.Contains(e.Id)).ToListAsync(ct);
        var positions = entries.Select(e => e.SortOrder).OrderBy(s => s).ToList();
        var ordered = ids.Select(id => entries.FirstOrDefault(e => e.Id == id)).Where(e => e is not null).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            // The moved entries share the positions they had, in the new order, so entries on other pages stay put.
            ordered[i]!.SortOrder = positions[i];
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds (tracked) the removal of every entry of a type and its values; the caller saves.
    /// </summary>
    public async Task DeleteAllAsync(ContentType type, CancellationToken ct = default)
    {
        _db.ContentEntryValues.RemoveRange(await _db.ContentEntryValues.Where(v => v.ContentTypeId == type.Id).ToListAsync(ct));
        _db.ContentEntries.RemoveRange(await _db.ContentEntries.Where(e => e.ContentTypeId == type.Id).ToListAsync(ct));
    }

    /// <summary>
    /// After a type's fields changed: moves every entry's values to the new fields (renamed fields keep their values,
    /// removed ones lose them) and rebuilds the derived columns and the index. Tracked; the caller saves.
    /// </summary>
    public async Task MigrateAsync(ContentType type, IReadOnlyList<FieldDefinition> before, IReadOnlyList<FieldDefinition> after, CancellationToken ct = default)
    {
        var entries = await _db.ContentEntries.Where(e => e.ContentTypeId == type.Id).ToListAsync(ct);
        _db.ContentEntryValues.RemoveRange(await _db.ContentEntryValues.Where(v => v.ContentTypeId == type.Id).ToListAsync(ct));
        foreach (var entry in entries)
        {
            var data = EntryData.Migrate(before, after, Parse(entry.DataJson));
            var json = data.ToString(Formatting.None);
            entry.DataJson = json;
            entry.Title = EntryData.Title(after, type.TitleField, data);
            entry.SearchText = EntryData.SearchText(after, entry.Title, data);
            AddValues(entry, after, data, published: false);
            if (entry.PublishedDataJson is not null)
            {
                var published = EntryData.Migrate(before, after, Parse(entry.PublishedDataJson));
                entry.PublishedDataJson = published.ToString(Formatting.None);
                AddValues(entry, after, published, published: true);
            }

            entry.HasDraft = !string.Equals(entry.DataJson, entry.PublishedDataJson, StringComparison.Ordinal);
        }
    }

    /// <summary>Validates input after looking up the media files and entries it refers to.</summary>
    public async Task<EntryValidation> ValidateAsync(ContentType type, IReadOnlyList<FieldDefinition> fields, JObject input, bool publish, CancellationToken ct = default)
    {
        var (mediaIds, entryRefs) = EntryData.References(fields, input);
        var media = new Dictionary<Guid, bool>();
        if (mediaIds.Count > 0)
        {
            var ids = mediaIds.ToList();
            foreach (var m in await _db.MediaFiles.AsNoTracking().Where(m => ids.Contains(m.Id))
                         .Select(m => new { m.Id, m.ContentType }).ToListAsync(ct))
            {
                media[m.Id] = m.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            }
        }

        var entries = new Dictionary<Guid, Guid>();
        if (entryRefs.Count > 0)
        {
            var ids = entryRefs.Select(r => r.Entry).Distinct().ToList();
            foreach (var e in await _db.ContentEntries.AsNoTracking().Where(e => ids.Contains(e.Id))
                         .Select(e => new { e.Id, e.ContentTypeId }).ToListAsync(ct))
            {
                entries[e.Id] = e.ContentTypeId;
            }
        }

        var references = new EntryReferences(
            (id, imagesOnly) => media.TryGetValue(id, out var isImage) && (!imagesOnly || isImage),
            (id, typeId) => entries.TryGetValue(id, out var actual) && actual == typeId);
        return EntryData.Validate(fields, input, publish, references);
    }

    private async Task<IReadOnlyList<FieldError>> UniqueErrorsAsync(ContentType type, IReadOnlyList<FieldDefinition> fields, JObject data, Guid? entryId, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        foreach (var f in fields.Where(f => f.Unique))
        {
            var value = EntryData.IndexValues(new[] { f }, data).FirstOrDefault();
            if (value is null)
            {
                continue;
            }

            var taken = f.Type == FieldTypes.Number
                ? await _db.ContentEntryValues.AnyAsync(v => v.ContentTypeId == type.Id && v.Field == f.Key && v.Number == value.Number && v.EntryId != entryId, ct)
                : await _db.ContentEntryValues.AnyAsync(v => v.ContentTypeId == type.Id && v.Field == f.Key && v.Text == value.Text && v.EntryId != entryId, ct);
            if (taken)
            {
                errors.Add(new FieldError(f.Key, $"{f.Label}: another entry already uses this value; it must be unique."));
            }
        }

        return errors;
    }

    /// <summary>The first of <c>value</c>, <c>value-2</c>, <c>value-3</c>... no entry of the type uses in <paramref name="field"/>.</summary>
    private async Task<string> FreeValueAsync(ContentType type, string field, string value, CancellationToken ct)
    {
        var taken = (await _db.ContentEntryValues.Where(v => v.ContentTypeId == type.Id && v.Field == field && v.Text != null && v.Text.StartsWith(EntryData.IndexText(value)))
            .Select(v => v.Text!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var candidate = value;
        for (var n = 2; taken.Contains(EntryData.IndexText(candidate)); n++)
        {
            candidate = $"{value}-{n}";
        }

        return candidate;
    }

    private ContentEntry NewEntry(ContentType type, IReadOnlyList<FieldDefinition> fields, JObject data, Guid? userId, int sortOrder)
    {
        var entry = new ContentEntry
        {
            ContentTypeId = type.Id,
            SortOrder = sortOrder,
            CreatedById = userId,
        };
        Apply(entry, type, fields, data.ToString(Formatting.None), data, userId);
        return entry;
    }

    private static void Apply(ContentEntry entry, ContentType type, IReadOnlyList<FieldDefinition> fields, string json, JObject data, Guid? userId)
    {
        entry.DataJson = json;
        entry.Title = EntryData.Title(fields, type.TitleField, data);
        entry.SearchText = EntryData.SearchText(fields, entry.Title, data);
        entry.HasDraft = !string.Equals(json, entry.PublishedDataJson, StringComparison.Ordinal);
        entry.Version = Guid.NewGuid();
        entry.UpdatedById = userId;
        entry.UpdatedDate = DateTime.UtcNow;
    }

    private async Task PublishDataAsync(ContentEntry entry, IReadOnlyList<FieldDefinition> fields, string json, JObject data, Guid? userId, CancellationToken ct)
    {
        if (entry.PublishedDataJson is not null)
        {
            await RemoveValuesAsync(entry.Id, published: true, ct);
        }

        entry.PublishedDataJson = json;
        entry.Published = true;
        entry.PublishedDate = DateTime.UtcNow;
        entry.PublishedById = userId;
        entry.HasDraft = !string.Equals(entry.DataJson, json, StringComparison.Ordinal);
        AddValues(entry, fields, data, published: true);
    }

    private void AddValues(ContentEntry entry, IReadOnlyList<FieldDefinition> fields, JObject data, bool published)
    {
        foreach (var v in EntryData.IndexValues(fields, data))
        {
            _db.ContentEntryValues.Add(new ContentEntryValue
            {
                ContentTypeId = entry.ContentTypeId,
                EntryId = entry.Id,
                Published = published,
                Field = v.Field,
                Text = v.Text,
                Number = v.Number,
                Date = v.Date,
            });
        }
    }

    /// <summary>Removes (tracked) an entry's index rows: of the working copy, the published values, or both (null).</summary>
    private async Task RemoveValuesAsync(Guid entryId, bool? published, CancellationToken ct)
    {
        var rows = await _db.ContentEntryValues.Where(v => v.EntryId == entryId).ToListAsync(ct);
        _db.ContentEntryValues.RemoveRange(rows.Where(v => published is null || v.Published == published));
    }

    /// <summary>An entry's stored JSON as an object (an empty one when it can't be read).</summary>
    public static JObject Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new JObject();
        }

        try
        {
            return JToken.Parse(json) as JObject ?? new JObject();
        }
        catch (JsonException)
        {
            return new JObject();
        }
    }

    /// <summary>The audit entity type of entries (the type's name is in the display, so a renamed type keeps one history).</summary>
    public const string AuditEntity = "Entry";

    /// <summary>"Products: Oak chair" - an entry as the audit log names it.</summary>
    public static string AuditDisplay(ContentType type, ContentEntry entry) => type.Name + ": " + Display(entry);

    /// <summary>How an entry is named in lists, the audit log and messages.</summary>
    public static string Display(ContentEntry entry) => entry.Title.Length > 0 ? entry.Title : "Untitled";
}

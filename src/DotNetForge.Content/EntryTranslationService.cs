using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Content;

/// <summary>
/// Translated entries (.docs/features/internationalization.md#entries), shared by the Content Manager, the page
/// renderer and the API: an entry of a type with <see cref="ContentType.Localized"/> keeps its translatable fields'
/// values per enabled locale in a <see cref="ContentEntryTranslation"/> (working copy and published values), every other
/// field shared. Publishing, unpublishing, duplicating, deleting and field changes of the entry carry its translations
/// along (<see cref="ContentEntryService"/>).
/// </summary>
public sealed class EntryTranslationService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly ContentEntryService _entries;

    public EntryTranslationService(DotNetForgeDbContext db, IAuditService audit, ContentEntryService entries)
    {
        _db = db;
        _audit = audit;
        _entries = entries;
    }

    /// <summary>The enabled locales other than the default - the ones an entry is translated into - in their order.</summary>
    public async Task<IReadOnlyList<Locale>> LocalesAsync(CancellationToken ct = default) =>
        await _db.Locales.AsNoTracking().Where(l => l.Enabled && !l.IsDefault).OrderBy(l => l.SortOrder).ThenBy(l => l.Code).ToListAsync(ct);

    /// <summary>An enabled locale by code (case-insensitive), the default included; null when there is none.</summary>
    public async Task<Locale?> FindLocaleAsync(string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var lower = code.Trim().ToLowerInvariant();
        return await _db.Locales.AsNoTracking().FirstOrDefaultAsync(l => (l.Enabled || l.IsDefault) && l.Code.ToLower() == lower, ct);
    }

    /// <summary>The translation of an entry into a locale (working copy and published), or null.</summary>
    public Task<ContentEntryTranslation?> FindAsync(Guid entryId, Guid localeId, CancellationToken ct = default) =>
        _db.ContentEntryTranslations.AsNoTracking().FirstOrDefaultAsync(t => t.EntryId == entryId && t.LocaleId == localeId, ct);

    /// <summary>
    /// Saves an entry's translatable values in a locale: validated like the entry's (lengths, rich text), empty values left
    /// out so they fall back. With <paramref name="publish"/> - or for a type without drafts - the values are published
    /// too. Returns the problems; nothing is saved when there are any.
    /// </summary>
    public async Task<IReadOnlyList<FieldError>> SaveAsync(ContentType type, ContentEntry entry, Locale locale, JObject input, bool publish,
        Guid? userId, CancellationToken ct = default)
    {
        var fields = EntryTranslations.Fields(ContentSchema.Parse(type.FieldsJson));
        var validation = await _entries.ValidateAsync(type, fields, input, publish: false, ct);
        if (validation.Errors.Count > 0)
        {
            return validation.Errors;
        }

        var json = EntryTranslations.Keep(fields, validation.Data).ToString(Formatting.None);
        var row = await _db.ContentEntryTranslations.FirstOrDefaultAsync(t => t.EntryId == entry.Id && t.LocaleId == locale.Id, ct);
        if (row is null)
        {
            row = new ContentEntryTranslation { EntryId = entry.Id, LocaleId = locale.Id };
            _db.ContentEntryTranslations.Add(row);
        }

        row.DataJson = json;
        if (publish || !type.Drafts)
        {
            row.PublishedDataJson = json;
        }

        row.UpdatedById = userId;
        row.UpdatedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.EntryTranslated, ContentEntryService.AuditEntity, entry.Id.ToString(), ContentEntryService.AuditDisplay(type, entry),
            details: $"{locale.DisplayName} ({locale.Code}){(publish || !type.Drafts ? ", published" : ", draft")}", cancellationToken: ct);
        return Array.Empty<FieldError>();
    }

    /// <summary>The published translated values of several entries in a locale (entry id → values), for lists and the API.</summary>
    public Task<IReadOnlyDictionary<Guid, JObject>> PublishedAsync(IReadOnlyCollection<Guid> entryIds, Guid localeId, CancellationToken ct = default) =>
        ValuesAsync(entryIds, localeId, published: true, ct);

    /// <summary>The translated values of several entries in a locale: published, or the working copies (entry id → values).</summary>
    public async Task<IReadOnlyDictionary<Guid, JObject>> ValuesAsync(IReadOnlyCollection<Guid> entryIds, Guid localeId, bool published, CancellationToken ct = default)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, JObject>();
        }

        var rows = await _db.ContentEntryTranslations.AsNoTracking()
            .Where(t => t.LocaleId == localeId && entryIds.Contains(t.EntryId))
            .Select(t => new { t.EntryId, Json = published ? t.PublishedDataJson : t.DataJson })
            .ToListAsync(ct);
        return rows.Where(r => r.Json != null).ToDictionary(r => r.EntryId, r => ContentEntryService.Parse(r.Json));
    }

    /// <summary>
    /// The default locale changed: per translated entry, the new default's values become the entry's own (title, search and
    /// index rebuilt) and the entry's previous values of those fields become the old default's translation. Saves.
    /// </summary>
    public async Task SwapDefaultAsync(Guid oldDefault, Guid newDefault, CancellationToken ct = default)
    {
        var promoted = await _db.ContentEntryTranslations.Where(t => t.LocaleId == newDefault).ToListAsync(ct);
        if (promoted.Count == 0)
        {
            return;
        }

        var ids = promoted.Select(t => t.EntryId).ToList();
        var entries = await _db.ContentEntries.Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var typeIds = entries.Values.Select(e => e.ContentTypeId).Distinct().ToList();
        var types = await _db.ContentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var demoted = await _db.ContentEntryTranslations.Where(t => t.LocaleId == oldDefault && ids.Contains(t.EntryId)).ToDictionaryAsync(t => t.EntryId, ct);
        foreach (var t in promoted)
        {
            _db.ContentEntryTranslations.Remove(t);
            if (!entries.TryGetValue(t.EntryId, out var entry) || !types.TryGetValue(entry.ContentTypeId, out var type))
            {
                continue;
            }

            var fields = ContentSchema.Parse(type.FieldsJson);
            var (working, oldWorking) = EntryTranslations.Swap(fields, ContentEntryService.Parse(entry.DataJson), ContentEntryService.Parse(t.DataJson));
            JObject? published = null;
            var oldPublished = new JObject();
            if (entry.PublishedDataJson is not null)
            {
                (published, oldPublished) = t.PublishedDataJson is null
                    ? (ContentEntryService.Parse(entry.PublishedDataJson), new JObject())
                    : EntryTranslations.Swap(fields, ContentEntryService.Parse(entry.PublishedDataJson), ContentEntryService.Parse(t.PublishedDataJson));
            }

            await _entries.ReplaceMainDataAsync(type, entry, working, published, ct);
            if (oldWorking.HasValues || oldPublished.HasValues)
            {
                var old = demoted.GetValueOrDefault(t.EntryId) ?? _db.ContentEntryTranslations.Add(new ContentEntryTranslation { EntryId = t.EntryId, LocaleId = oldDefault }).Entity;
                old.DataJson = oldWorking.ToString(Formatting.None);
                old.PublishedDataJson = entry.PublishedDataJson is null ? null : oldPublished.ToString(Formatting.None);
                old.UpdatedDate = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}

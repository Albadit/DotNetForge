using System.Text.RegularExpressions;
using DotNetForge.Core.Content;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetForge.Content;

/// <summary>What the Content types builder posts for one type.</summary>
public sealed class ContentTypeInput
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Icon { get; set; } = "database";

    public ContentTypeKind Kind { get; set; }

    public bool Drafts { get; set; } = true;

    public string? TitleField { get; set; }

    /// <summary>The field definitions as JSON (the builder serializes its state into this).</summary>
    public string FieldsJson { get; set; } = "[]";

    public static ContentTypeInput From(ContentType t) => new()
    {
        Name = t.Name,
        Slug = t.Slug,
        Description = t.Description,
        Icon = t.Icon,
        Kind = t.Kind,
        Drafts = t.Drafts,
        TitleField = t.TitleField,
        FieldsJson = t.FieldsJson,
    };
}

/// <summary>
/// The rules for content types (.docs/features/structured-content.md#content-types): names and API ids, the field
/// definitions (checked by <see cref="ContentSchema"/>), what happens to existing entries when fields change, and when
/// a type may be deleted. Methods validate and change tracked entities; the callers here save and audit, since a type
/// change may rewrite many entries in one transaction.
/// </summary>
public sealed partial class ContentTypeService
{
    /// <summary>API ids the Content Manager's own routes use.</summary>
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.Ordinal) { "api", "types", "new", "media" };

    /// <summary>Icons offered for a content type (all in the sprite).</summary>
    public static readonly IReadOnlyList<string> Icons = new[]
    {
        "database", "pages", "tag", "users", "store", "calendar", "briefcase", "message", "help", "settings", "image",
        "star", "globe", "mail", "file-code", "activity",
    };

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly ContentEntryService _entries;

    public ContentTypeService(DotNetForgeDbContext db, IAuditService audit, ContentEntryService entries)
    {
        _db = db;
        _audit = audit;
        _entries = entries;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    /// <summary>The types in their Content Manager order.</summary>
    public Task<List<ContentType>> ListAsync(CancellationToken ct = default) =>
        _db.ContentTypes.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);

    public Task<ContentType?> FindAsync(string slug, CancellationToken ct = default) =>
        _db.ContentTypes.FirstOrDefaultAsync(t => t.Slug == slug, ct);

    public Task<ContentType?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.ContentTypes.FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>
    /// Creates a type from a template (and the types it links to, when they are missing) or an empty one named
    /// <paramref name="name"/>. Returns the new type or an error.
    /// </summary>
    public async Task<(ContentType? Type, string? Error)> CreateAsync(string? name, string? templateId, Guid? userId, CancellationToken ct = default)
    {
        var existing = await _db.ContentTypes.Select(t => new { t.Id, t.Slug, t.SortOrder }).ToListAsync(ct);
        var slugs = existing.ToDictionary(t => t.Slug, t => t.Id, StringComparer.Ordinal);
        var order = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder) + 1;
        var created = new List<ContentType>();

        ContentType Add(ContentTypeTemplate template, string? displayName)
        {
            var slug = FreeSlug(displayName is null ? template.Slug : EntryData.Slugify(displayName), slugs.Keys);
            var type = new ContentType
            {
                Name = displayName?.Trim() is { Length: > 0 } n ? n : template.Name,
                Slug = slug,
                Description = template.Description,
                Icon = template.Icon,
                Kind = template.Kind,
                TitleField = template.TitleField,
                SortOrder = order++,
                CreatedById = userId,
            };
            slugs[slug] = type.Id;
            created.Add(type);
            // Relationship targets: the type with the template's API id, created from its template when missing.
            foreach (var required in template.Requires)
            {
                if (!slugs.ContainsKey(required) && ContentTypeTemplates.Find(required) is { } dependency)
                {
                    Add(dependency, null);
                }
            }

            type.FieldsJson = ContentSchema.Serialize(ContentSchema.Validate(template.Fields(s => slugs[s]), _ => true).Fields);
            return type;
        }

        ContentType main;
        if (ContentTypeTemplates.Find(templateId) is { } chosen)
        {
            main = Add(chosen, string.IsNullOrWhiteSpace(name) ? null : name);
        }
        else
        {
            var display = name?.Trim() ?? string.Empty;
            if (display.Length == 0)
            {
                return (null, "Give the content type a name.");
            }

            if (display.Length > 100)
            {
                return (null, "The name must be at most 100 characters.");
            }

            var slug = FreeSlug(EntryData.Slugify(display), slugs.Keys);
            main = new ContentType
            {
                Name = display,
                Slug = slug,
                SortOrder = order,
                CreatedById = userId,
                FieldsJson = ContentSchema.Serialize(ContentSchema.Validate(new JArray(new JObject
                {
                    ["key"] = "title",
                    ["label"] = "Title",
                    ["type"] = FieldTypes.Text,
                    ["required"] = true,
                    ["showInList"] = true,
                }), _ => true).Fields),
                TitleField = "title",
            };
            created.Add(main);
        }

        _db.ContentTypes.AddRange(created);
        foreach (var type in created.Where(t => t.Kind == ContentTypeKind.Single))
        {
            _entries.AddSingleEntry(type, userId);
        }

        await _db.SaveChangesAsync(ct);
        foreach (var type in created)
        {
            await _audit.LogAsync(AuditActions.ContentTypeCreated, "ContentType", type.Id.ToString(), type.Name, cancellationToken: ct);
        }

        return (main, null);
    }

    /// <summary>
    /// Validates <paramref name="input"/> and applies it to <paramref name="type"/>. When fields changed, every entry's
    /// values follow (renamed keys keep their values, removed fields drop theirs) and the entries are re-indexed.
    /// Saves and audits. Returns the problems, empty when saved.
    /// </summary>
    public async Task<IReadOnlyList<string>> UpdateAsync(ContentType type, ContentTypeInput input, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add("The name is required.");
        }
        else if (name.Length > 100)
        {
            errors.Add("The name must be at most 100 characters.");
        }

        var slug = (input.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(slug))
        {
            errors.Add("The API id must start with a letter and use lower-case letters, digits and hyphens (at most 64).");
        }
        else if (ReservedSlugs.Contains(slug))
        {
            errors.Add($"\"{slug}\" is reserved; choose another API id.");
        }
        else if (await _db.ContentTypes.AnyAsync(t => t.Slug == slug && t.Id != type.Id, ct))
        {
            errors.Add($"Another content type already uses the API id \"{slug}\".");
        }

        var description = input.Description?.Trim();
        if (description?.Length > 500)
        {
            errors.Add("The description must be at most 500 characters.");
        }

        var icon = Icons.Contains(input.Icon) ? input.Icon : "database";

        if (input.Kind == ContentTypeKind.Single && type.Kind == ContentTypeKind.Collection
            && await _db.ContentEntries.CountAsync(e => e.ContentTypeId == type.Id, ct) > 1)
        {
            errors.Add("This type has more than one entry, so it can't become a single type. Delete the others first.");
        }

        JToken? fieldsJson;
        try
        {
            fieldsJson = JToken.Parse(string.IsNullOrWhiteSpace(input.FieldsJson) ? "[]" : input.FieldsJson);
        }
        catch (JsonException)
        {
            fieldsJson = null;
        }

        var typeIds = (await _db.ContentTypes.Select(t => t.Id).ToListAsync(ct)).ToHashSet();
        var schema = ContentSchema.Validate(fieldsJson, typeIds.Contains);
        errors.AddRange(schema.Errors);
        if (schema.IsValid && schema.Fields.Count == 0)
        {
            errors.Add("Add at least one field.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var before = ContentSchema.Parse(type.FieldsJson);
        var newFieldsJson = ContentSchema.Serialize(schema.Fields);
        var fieldsChanged = !string.Equals(newFieldsJson, ContentSchema.Serialize(before), StringComparison.Ordinal);
        var titleKey = ContentSchema.TitleKey(schema.Fields, input.TitleField);
        var titleChanged = !string.Equals(titleKey, ContentSchema.TitleKey(before, type.TitleField), StringComparison.Ordinal);

        type.Name = name;
        type.Slug = slug;
        type.Description = string.IsNullOrEmpty(description) ? null : description;
        type.Icon = icon;
        type.Kind = input.Kind;
        type.Drafts = input.Drafts;
        type.TitleField = titleKey;
        type.FieldsJson = newFieldsJson;
        type.UpdatedDate = DateTime.UtcNow;

        if (type.Kind == ContentTypeKind.Single && !await _db.ContentEntries.AnyAsync(e => e.ContentTypeId == type.Id, ct))
        {
            _entries.AddSingleEntry(type, null);
        }

        // One save: the type and every rewritten entry change together (or not at all).
        if (fieldsChanged || titleChanged)
        {
            await _entries.MigrateAsync(type, before, schema.Fields, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditActions.ContentTypeUpdated, "ContentType", type.Id.ToString(), type.Name,
            details: fieldsChanged ? $"{schema.Fields.Count} field(s)" : null, cancellationToken: ct);
        return errors;
    }

    /// <summary>
    /// Deletes a type. Refused while another type links to it, and while it has entries unless
    /// <paramref name="withEntries"/> (then the entries go too). Saves and audits. Returns the reason it was refused.
    /// </summary>
    public async Task<string?> DeleteAsync(ContentType type, bool withEntries, CancellationToken ct = default)
    {
        var others = await _db.ContentTypes.AsNoTracking()
            .Where(t => t.Id != type.Id)
            .Select(t => new { t.Name, t.FieldsJson })
            .ToListAsync(ct);
        var linking = others.FirstOrDefault(t => LinksTo(ContentSchema.Parse(t.FieldsJson), type.Id));
        if (linking is not null)
        {
            return $"\"{linking.Name}\" has a relationship field that links to this type. Remove that field first.";
        }

        var count = await _db.ContentEntries.CountAsync(e => e.ContentTypeId == type.Id, ct);
        if (count > 0 && !withEntries && type.Kind == ContentTypeKind.Collection)
        {
            return $"This type has {count} entr{(count == 1 ? "y" : "ies")}. Delete them too, or keep the type.";
        }

        await _entries.DeleteAllAsync(type, ct);
        _db.ContentTypes.Remove(type);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(AuditActions.ContentTypeDeleted, "ContentType", type.Id.ToString(), type.Name,
            details: count > 0 ? $"With {count} entr{(count == 1 ? "y" : "ies")}" : null, cancellationToken: ct);
        return null;
    }

    private static bool LinksTo(IReadOnlyList<FieldDefinition> fields, Guid typeId) =>
        fields.Any(f => f.RelationTo == typeId || LinksTo(f.Fields, typeId));

    private static string FreeSlug(string wanted, IEnumerable<string> taken)
    {
        var used = taken.ToHashSet(StringComparer.Ordinal);
        var slug = wanted.Length == 0 || !char.IsAsciiLetterLower(wanted[0]) ? "type-" + wanted : wanted;
        slug = slug.Length > 56 ? slug[..56].TrimEnd('-') : slug;
        if (ReservedSlugs.Contains(slug))
        {
            slug += "-type";
        }

        var candidate = slug;
        for (var n = 2; used.Contains(candidate); n++)
        {
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }
}

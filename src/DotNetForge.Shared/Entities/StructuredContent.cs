using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// A content type defined in the Content types builder (.docs/features/structured-content.md): a name, an
/// API id and its fields. A <see cref="ContentTypeKind.Collection"/> holds many entries, a
/// <see cref="ContentTypeKind.Single"/> exactly one. The field list is stored as JSON (<see cref="FieldsJson"/>) and is
/// the only definition of the entries' shape - nothing about a type is hard-coded.
/// </summary>
public class ContentType
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, plural for collections ("Blog posts").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The API id: lower-case letters, digits and hyphens, unique (<c>/api/entries/{slug}</c>).</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>A sprite icon name (<c>wwwroot/img/icons.svg</c>) shown in the admin.</summary>
    public string Icon { get; set; } = "database";

    public ContentTypeKind Kind { get; set; } = ContentTypeKind.Collection;

    /// <summary>The field definitions as JSON (an array, see <c>ContentSchema</c>).</summary>
    public string FieldsJson { get; set; } = "[]";

    /// <summary>The key of the top-level field whose value is an entry's title; null = the first text field.</summary>
    public string? TitleField { get; set; }

    /// <summary>
    /// On: saving keeps changes as a draft until someone publishes them. Off: saving publishes at once (still
    /// validated as a publish).
    /// </summary>
    public bool Drafts { get; set; } = true;

    /// <summary>Position in the Content Manager's list of types.</summary>
    public int SortOrder { get; set; }

    public Guid? CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One entry of a <see cref="ContentType"/>: its field values as JSON. <see cref="DataJson"/> is the working copy the
/// editor saves; <see cref="PublishedDataJson"/> is what the site and the API show (null = not published). Display
/// columns (<see cref="Title"/>, <see cref="SearchText"/>) are copies made on save, for lists and search.
/// </summary>
public class ContentEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContentTypeId { get; set; }

    /// <summary>The title field's text (max 300), shown in lists and relationship pickers.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Lower-cased title and short text values (max 1000) for the admin search.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>The working copy (latest saved values).</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>The published values, or null when the entry is not published.</summary>
    public string? PublishedDataJson { get; set; }

    /// <summary>Whether <see cref="PublishedDataJson"/> is set (kept as a column so lists can filter on it).</summary>
    public bool Published { get; set; }

    /// <summary>Whether the working copy differs from the published values (or nothing is published yet).</summary>
    public bool HasDraft { get; set; } = true;

    public DateTime? PublishedDate { get; set; }

    public Guid? PublishedById { get; set; }

    /// <summary>Manual order (the default sort of lists and content blocks).</summary>
    public int SortOrder { get; set; }

    /// <summary>Changes on every save; the editor posts the version it loaded so a stale save is refused.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public Guid? CreatedById { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public Guid? UpdatedById { get; set; }

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One indexed value of an entry: a scalar top-level field (text, number, date, choice, relationship id...) copied out
/// of the JSON so lists and content blocks can filter, sort and check uniqueness with single-table queries on every
/// database (.docs/features/structured-content.md#querying). Rewritten whenever the entry is saved or published.
/// </summary>
public class ContentEntryValue
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContentTypeId { get; set; }

    public Guid EntryId { get; set; }

    /// <summary>True: a value of the published version; false: of the working copy.</summary>
    public bool Published { get; set; }

    /// <summary>The field key (a multi-value field has one row per value).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>The value as lower-cased text (max 200): equality, "contains" and text sorting.</summary>
    public string? Text { get; set; }

    /// <summary>Numbers (and booleans as 0/1) for numeric comparison and sorting.</summary>
    public double? Number { get; set; }

    /// <summary>Dates (UTC) for date comparison and sorting.</summary>
    public DateTime? Date { get; set; }
}

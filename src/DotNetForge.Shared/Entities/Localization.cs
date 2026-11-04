namespace DotNetForge.Shared.Entities;

/// <summary>
/// A language (and optionally region or script) the site offers (.docs/features/internationalization.md). Exactly one
/// locale is the default - the language of unprefixed public URLs and the fallback of every missing translation; the
/// others are reached under their code (<c>/de/about</c>) while enabled.
/// </summary>
public class Locale
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The BCP-47 tag in its canonical case (<c>en</c>, <c>de-DE</c>, <c>zh-Hans</c>); unique.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The name shown in the admin and the public language switcher ("Deutsch").</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The fallback locale; exactly one row has it (a filtered unique index makes a second one impossible).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Offered for translation and on the public site; the default locale is always enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Position in lists and the language switcher.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A content page's text in a locale other than the default (.docs/features/internationalization.md#content-pages):
/// the same page and URL, its own title, SEO texts and layout. An empty field falls back to the page's own value.
/// </summary>
public class PageTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PageId { get; set; }
    public Guid LocaleId { get; set; }

    public string? Title { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    /// <summary>The published layout in this locale (page builder JSON); null = the default locale's layout.</summary>
    public string? LayoutJson { get; set; }

    /// <summary>The unpublished draft of <see cref="LayoutJson"/>; null when there are no unpublished changes.</summary>
    public string? DraftLayoutJson { get; set; }

    /// <summary>The page builder's draft bookkeeping, as on <see cref="Page"/> (conflict check, who saved, when published).</summary>
    public Guid? DraftVersion { get; set; }
    public DateTime? DraftUpdatedDate { get; set; }
    public Guid? DraftUpdatedById { get; set; }
    public DateTime? LayoutPublishedDate { get; set; }

    public Guid? UpdatedById { get; set; }
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// An entry's values in a locale other than the default (.docs/features/internationalization.md#entries), for the
/// localizable fields of a type with <see cref="ContentType.Localized"/>: a working copy and the published values, like
/// the entry itself. A missing or empty value falls back to the entry's own (default locale) value.
/// </summary>
public class ContentEntryTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    public Guid LocaleId { get; set; }

    /// <summary>The working copy of the translated values (a JSON object of field key → value).</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>The published translated values; null until the entry is published with this translation.</summary>
    public string? PublishedDataJson { get; set; }

    public Guid? UpdatedById { get; set; }
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

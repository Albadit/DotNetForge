using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services.Localization;

/// <summary>A page's texts in one locale, as the Translations tab posts them.</summary>
public sealed class PageTranslationInput
{
    public Guid LocaleId { get; set; }
    public string? Title { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}

/// <summary>A page's translation into one enabled locale, for the Translations tab.</summary>
public sealed record PageTranslationView(LocaleInfo Locale, string? Title, string? MetaTitle, string? MetaDescription,
    bool HasLayout, bool HasDraftChanges, DateTime? LayoutPublishedDate, DateTime? DraftUpdatedDate);

/// <summary>
/// Translated content pages (.docs/features/internationalization.md#content-pages): a page's own fields are the default
/// locale's; a <see cref="PageTranslation"/> holds another locale's title, SEO texts and layout, each falling back to the
/// page's value when empty. Overlaying on the public site, the menu and language links, the Translations tab, the page
/// builder's per-locale layouts, and moving the content when the default locale changes.
/// </summary>
public sealed class PageTranslationService : IDefaultLocaleSwap
{
    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly LocaleCatalog _locales;

    public PageTranslationService(DotNetForgeDbContext db, IAuditService audit, LocaleCatalog locales)
    {
        _db = db;
        _audit = audit;
        _locales = locales;
    }

    /// <summary>
    /// Shows <paramref name="page"/> (an untracked copy) in <paramref name="locale"/>: its translated title, SEO texts and
    /// published layout replace the page's own where set. A translated title without its own meta title makes the meta
    /// title the translated title, not the default locale's. The canonical URL is left out (it names the default URL).
    /// </summary>
    public async Task TranslateAsync(Page page, LocaleInfo locale, CancellationToken ct = default)
    {
        if (locale.IsDefault)
        {
            return;
        }

        page.CanonicalUrl = null;
        var t = await _db.PageTranslations.AsNoTracking().FirstOrDefaultAsync(x => x.PageId == page.Id && x.LocaleId == locale.Id, ct);
        if (t is null)
        {
            return;
        }

        page.MetaTitle = Text(t.MetaTitle) ?? (Text(t.Title) is null ? page.MetaTitle : null);
        page.Title = Text(t.Title) ?? page.Title;
        page.MetaDescription = Text(t.MetaDescription) ?? page.MetaDescription;
        page.LayoutJson = t.LayoutJson ?? page.LayoutJson;
    }

    /// <summary>The menu in <paramref name="locale"/>: translated titles, paths under the locale's prefix.</summary>
    public async Task<IReadOnlyList<PublishedPage>> LocalizeAsync(IReadOnlyList<PublishedPage> pages, LocaleInfo locale, CancellationToken ct = default)
    {
        if (locale.IsDefault || pages.Count == 0)
        {
            return pages;
        }

        var ids = pages.Select(p => p.Id).ToList();
        var titles = await _db.PageTranslations.AsNoTracking()
            .Where(t => t.LocaleId == locale.Id && ids.Contains(t.PageId) && t.Title != null && t.Title != "")
            .ToDictionaryAsync(t => t.PageId, t => t.Title!, ct);
        return pages.Select(p => p with
        {
            Title = titles.GetValueOrDefault(p.Id, p.Title),
            Path = RequestLocale.UrlIn(locale, p.Path),
        }).ToList();
    }

    /// <summary>The page at <paramref name="path"/> (unprefixed) in every enabled locale, the request's marked current.</summary>
    public static IReadOnlyList<LanguageLink> Languages(RequestLocale request, string path) =>
        request.Locales.Enabled.Count < 2
            ? Array.Empty<LanguageLink>()
            : request.Locales.Enabled
                .Select(l => new LanguageLink(l.Code, l.DisplayName, RequestLocale.UrlIn(l, path), l.Id == request.Locale.Id))
                .ToList();

    /// <summary>The page's translations into every enabled locale other than the default, for the Translations tab.</summary>
    public async Task<IReadOnlyList<PageTranslationView>> ForPageAsync(Guid pageId, CancellationToken ct = default)
    {
        var locales = (await _locales.GetAsync(ct)).Translations.ToList();
        var rows = await _db.PageTranslations.AsNoTracking().Where(t => t.PageId == pageId).ToListAsync(ct);
        return locales.Select(l => rows.FirstOrDefault(r => r.LocaleId == l.Id) is { } t
                ? new PageTranslationView(l, t.Title, t.MetaTitle, t.MetaDescription, t.LayoutJson is not null,
                    t.DraftLayoutJson is not null && t.DraftLayoutJson != t.LayoutJson, t.LayoutPublishedDate, t.DraftUpdatedDate)
                : new PageTranslationView(l, null, null, null, false, false, null, null))
            .ToList();
    }

    /// <summary>
    /// Saves the Translations tab: validates the lengths, upserts each locale's texts and audits the changed ones. Not
    /// saved (the caller saves with the page). Returns an error, or null.
    /// </summary>
    public async Task<string?> ApplyTextsAsync(Page page, IReadOnlyList<PageTranslationInput> inputs, Guid? userId, CancellationToken ct = default)
    {
        var locales = (await _locales.GetAsync(ct)).Translations.ToDictionary(l => l.Id);
        foreach (var input in inputs)
        {
            if (!locales.TryGetValue(input.LocaleId, out var locale))
            {
                continue; // disabled or deleted meanwhile: its translation is left as it was
            }

            if (Text(input.Title)?.Length > 300 || Text(input.MetaTitle)?.Length > 300 || Text(input.MetaDescription)?.Length > 1000)
            {
                return $"{locale.DisplayName}: the title and meta title can be at most 300 characters, the meta description 1000.";
            }

            var row = await _db.PageTranslations.FirstOrDefaultAsync(t => t.PageId == page.Id && t.LocaleId == locale.Id, ct);
            var (title, metaTitle, metaDescription) = (Text(input.Title), Text(input.MetaTitle), Text(input.MetaDescription));
            if (row is null)
            {
                if (title is null && metaTitle is null && metaDescription is null)
                {
                    continue;
                }

                row = new PageTranslation { PageId = page.Id, LocaleId = locale.Id };
                _db.PageTranslations.Add(row);
            }
            else if (row.Title == title && row.MetaTitle == metaTitle && row.MetaDescription == metaDescription)
            {
                continue;
            }

            (row.Title, row.MetaTitle, row.MetaDescription) = (title, metaTitle, metaDescription);
            row.UpdatedById = userId;
            row.UpdatedDate = DateTime.UtcNow;
            await _audit.LogAsync(AuditActions.ContentTranslated, "Page", page.Id.ToString(), page.Title,
                details: $"{locale.DisplayName} ({locale.Code}): title and SEO texts", cancellationToken: ct);
        }

        return null;
    }

    /// <summary>The tracked translation of a page into a locale, created (unsaved) when there is none.</summary>
    public async Task<PageTranslation> GetOrAddAsync(Guid pageId, Guid localeId, CancellationToken ct = default)
    {
        var row = await _db.PageTranslations.FirstOrDefaultAsync(t => t.PageId == pageId && t.LocaleId == localeId, ct);
        if (row is null)
        {
            row = new PageTranslation { PageId = pageId, LocaleId = localeId };
            _db.PageTranslations.Add(row);
        }

        return row;
    }

    /// <summary>
    /// Puts a translation's layout state onto an untracked copy of its page, so the page builder edits it as it edits a
    /// page. A locale without a layout yet starts from the page's own layout (draft, else published) as an unsaved draft.
    /// </summary>
    public static void ShowLayout(Page page, PageTranslation t)
    {
        var untouched = t.LayoutJson is null && t.DraftLayoutJson is null;
        page.Title = Text(t.Title) ?? page.Title;
        page.DraftLayoutJson = untouched ? PageBuilderService.EditableJson(page) : t.DraftLayoutJson;
        page.LayoutJson = t.LayoutJson;
        page.DraftVersion = untouched ? null : t.DraftVersion;
        page.DraftUpdatedDate = untouched ? null : t.DraftUpdatedDate;
        page.DraftUpdatedById = untouched ? null : t.DraftUpdatedById;
        page.LayoutPublishedDate = t.LayoutPublishedDate;
    }

    /// <summary>Takes the layout state the page builder left on the page copy back into the translation.</summary>
    public static void KeepLayout(Page page, PageTranslation t, Guid? userId)
    {
        t.LayoutJson = page.LayoutJson;
        t.DraftLayoutJson = page.DraftLayoutJson;
        t.DraftVersion = page.DraftVersion;
        t.DraftUpdatedDate = page.DraftUpdatedDate;
        t.DraftUpdatedById = page.DraftUpdatedById;
        t.LayoutPublishedDate = page.LayoutPublishedDate;
        t.UpdatedById = userId;
        t.UpdatedDate = DateTime.UtcNow;
    }

    /// <summary>
    /// The default locale changed: per page, the new default's translated fields become the page's own, and the page's
    /// old values become the old default's translation. Untranslated fields stay as they are (they showed through anyway).
    /// </summary>
    public async Task SwapAsync(Locale oldDefault, Locale newDefault, CancellationToken ct)
    {
        var promoted = await _db.PageTranslations.Where(t => t.LocaleId == newDefault.Id).ToListAsync(ct);
        if (promoted.Count == 0)
        {
            return;
        }

        var ids = promoted.Select(t => t.PageId).ToList();
        var pages = await _db.Pages.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var demotedRows = await _db.PageTranslations.Where(t => t.LocaleId == oldDefault.Id && ids.Contains(t.PageId)).ToDictionaryAsync(t => t.PageId, ct);
        foreach (var t in promoted)
        {
            if (!pages.TryGetValue(t.PageId, out var page))
            {
                _db.PageTranslations.Remove(t);
                continue;
            }

            var old = demotedRows.GetValueOrDefault(t.PageId) ?? new PageTranslation { PageId = t.PageId, LocaleId = oldDefault.Id };
            if (Text(t.Title) is { } title)
            {
                (old.Title, page.Title) = (page.Title, title);
            }

            if (Text(t.MetaTitle) is { } metaTitle)
            {
                (old.MetaTitle, page.MetaTitle) = (page.MetaTitle, metaTitle);
            }

            if (Text(t.MetaDescription) is { } metaDescription)
            {
                (old.MetaDescription, page.MetaDescription) = (page.MetaDescription, metaDescription);
            }

            if (t.LayoutJson is not null || t.DraftLayoutJson is not null)
            {
                var saved = new PageTranslation();
                KeepLayout(page, saved, page.DraftUpdatedById);
                CopyLayout(t, page);
                CopyLayout(saved, old);
            }

            page.UpdatedDate = DateTime.UtcNow;
            _db.PageTranslations.Remove(t);
            if (!demotedRows.ContainsKey(t.PageId) && (old.Title ?? old.MetaTitle ?? old.MetaDescription ?? old.LayoutJson ?? old.DraftLayoutJson) is not null)
            {
                _db.PageTranslations.Add(old);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static void CopyLayout(PageTranslation from, Page to)
    {
        to.LayoutJson = from.LayoutJson;
        to.DraftLayoutJson = from.DraftLayoutJson;
        to.DraftVersion = from.DraftVersion;
        to.DraftUpdatedDate = from.DraftUpdatedDate;
        to.DraftUpdatedById = from.DraftUpdatedById;
        to.LayoutPublishedDate = from.LayoutPublishedDate;
    }

    private static void CopyLayout(PageTranslation from, PageTranslation to)
    {
        to.LayoutJson = from.LayoutJson;
        to.DraftLayoutJson = from.DraftLayoutJson;
        to.DraftVersion = from.DraftVersion;
        to.DraftUpdatedDate = from.DraftUpdatedDate;
        to.DraftUpdatedById = from.DraftUpdatedById;
        to.LayoutPublishedDate = from.LayoutPublishedDate;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

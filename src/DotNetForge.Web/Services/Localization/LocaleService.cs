using DotNetForge.Core.Validation;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services.Localization;

/// <summary>A locale on the Internationalization screen, with how much content is translated into it.</summary>
public sealed record LocaleRow(Guid Id, string Code, string DisplayName, bool IsDefault, bool Enabled, int Pages, int Entries);

/// <summary>
/// Every locale rule (.docs/features/internationalization.md#rules-and-validation): valid, unique codes; exactly one
/// default, always enabled, never deleted; an atomic default switch that also moves the content
/// (<see cref="IDefaultLocaleSwap"/>); deleting a locale deletes its translations. Each change is audited and clears
/// <see cref="LocaleCatalog"/>. Methods return an error message, or null on success.
/// </summary>
public sealed class LocaleService
{
    public const int MaxDisplayName = 100;

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly LocaleCatalog _catalog;
    private readonly IEnumerable<IDefaultLocaleSwap> _swaps;

    public LocaleService(DotNetForgeDbContext db, IAuditService audit, LocaleCatalog catalog, IEnumerable<IDefaultLocaleSwap> swaps)
    {
        _db = db;
        _audit = audit;
        _catalog = catalog;
        _swaps = swaps;
    }

    /// <summary>The locales in their order (the default first), with their translated pages and entries.</summary>
    public async Task<IReadOnlyList<LocaleRow>> ListAsync(CancellationToken ct = default)
    {
        var pages = await _db.PageTranslations.AsNoTracking().GroupBy(t => t.LocaleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var entries = await _db.ContentEntryTranslations.AsNoTracking().GroupBy(t => t.LocaleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await _db.Locales.AsNoTracking().OrderByDescending(l => l.IsDefault).ThenBy(l => l.SortOrder).ThenBy(l => l.Code).ToListAsync(ct))
            .Select(l => new LocaleRow(l.Id, l.Code, l.DisplayName, l.IsDefault, l.Enabled,
                pages.GetValueOrDefault(l.Id), entries.GetValueOrDefault(l.Id)))
            .ToList();
    }

    /// <summary>Adds a locale (never the default). An empty display name becomes the language's own name.</summary>
    public async Task<string?> AddAsync(string? code, string? displayName, bool enabled, CancellationToken ct = default)
    {
        var normalized = LocaleCodes.Normalize(code, out var error);
        if (normalized is null)
        {
            return error;
        }

        if (await _db.Locales.AnyAsync(l => l.Code.ToLower() == normalized.ToLower(), ct))
        {
            return $"The locale {normalized} already exists.";
        }

        // Its code becomes a URL prefix, which would hide a top-level page with the same slug.
        var prefix = normalized.ToLowerInvariant();
        if (await _db.Pages.AnyAsync(p => p.ParentPageId == null && p.Slug == prefix, ct))
        {
            return $"A top-level page uses the slug '{prefix}', which this locale's URLs would hide. Change that page's slug first.";
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? LocaleCodes.SuggestedName(normalized) : displayName.Trim();
        if (name.Length > MaxDisplayName)
        {
            return $"The display name can be at most {MaxDisplayName} characters.";
        }

        var order = await _db.Locales.MaxAsync(l => (int?)l.SortOrder, ct) ?? 0;
        var locale = new Locale { Code = normalized, DisplayName = name, Enabled = enabled, IsDefault = false, SortOrder = order + 1 };
        _db.Locales.Add(locale);
        await _db.SaveChangesAsync(ct);
        _catalog.Invalidate();
        await _audit.LogAsync(AuditActions.LocaleCreated, "Locale", locale.Id.ToString(), $"{locale.DisplayName} ({locale.Code})",
            details: enabled ? "Enabled" : "Disabled", cancellationToken: ct);
        return null;
    }

    /// <summary>Changes the display name and enabled state. The default locale stays enabled.</summary>
    public async Task<string?> UpdateAsync(Guid id, string? displayName, bool enabled, CancellationToken ct = default)
    {
        var locale = await _db.Locales.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (locale is null)
        {
            return "That locale no longer exists.";
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Enter a display name.";
        }

        if (displayName.Trim().Length > MaxDisplayName)
        {
            return $"The display name can be at most {MaxDisplayName} characters.";
        }

        if (locale.IsDefault && !enabled)
        {
            return $"{locale.DisplayName} is the default locale and stays enabled. Make another locale the default first.";
        }

        var changes = new List<string>();
        if (locale.DisplayName != displayName.Trim())
        {
            changes.Add($"name \"{locale.DisplayName}\" → \"{displayName.Trim()}\"");
            locale.DisplayName = displayName.Trim();
        }

        if (locale.Enabled != enabled)
        {
            changes.Add(enabled ? "enabled" : "disabled");
            locale.Enabled = enabled;
        }

        if (changes.Count == 0)
        {
            return null;
        }

        await _db.SaveChangesAsync(ct);
        _catalog.Invalidate();
        await _audit.LogAsync(AuditActions.LocaleUpdated, "Locale", locale.Id.ToString(), $"{locale.DisplayName} ({locale.Code})",
            details: string.Join(", ", changes), cancellationToken: ct);
        return null;
    }

    /// <summary>
    /// Makes the locale the default, in one transaction: the old default is demoted, the new one promoted (and enabled),
    /// and the content moves with it (<see cref="IDefaultLocaleSwap"/>). The filtered unique index on <c>IsDefault</c>
    /// refuses a concurrent switch, so there is always exactly one default.
    /// </summary>
    public async Task<string?> SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        var locale = await _db.Locales.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (locale is null)
        {
            return "That locale no longer exists.";
        }

        if (locale.IsDefault)
        {
            return null;
        }

        var previous = await _db.Locales.FirstOrDefaultAsync(l => l.IsDefault, ct);
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (previous is not null)
            {
                previous.IsDefault = false;
                await _db.SaveChangesAsync(ct); // demote first: the index allows one default at a time
            }

            locale.IsDefault = true;
            locale.Enabled = true;
            await _db.SaveChangesAsync(ct);
            if (previous is not null)
            {
                foreach (var swap in _swaps)
                {
                    await swap.SwapAsync(previous, locale, ct);
                }
            }

            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            _catalog.Invalidate();
            return "The default locale was changed at the same time by someone else. Reload and try again.";
        }

        _catalog.Invalidate();
        await _audit.LogAsync(AuditActions.LocaleDefaultChanged, "Locale", locale.Id.ToString(), $"{locale.DisplayName} ({locale.Code})",
            details: previous is null ? null : $"Was {previous.DisplayName} ({previous.Code})", cancellationToken: ct);
        return null;
    }

    /// <summary>Deletes a locale and its translations. The default (and so the last) locale can't be deleted.</summary>
    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var locale = await _db.Locales.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (locale is null)
        {
            return "That locale no longer exists.";
        }

        if (locale.IsDefault)
        {
            return $"{locale.DisplayName} is the default locale. Make another locale the default before deleting it.";
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var pages = await _db.PageTranslations.Where(t => t.LocaleId == id).ExecuteDeleteAsync(ct);
        var entries = await _db.ContentEntryTranslations.Where(t => t.LocaleId == id).ExecuteDeleteAsync(ct);
        _db.Locales.Remove(locale);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _catalog.Invalidate();
        await _audit.LogAsync(AuditActions.LocaleDeleted, "Locale", locale.Id.ToString(), $"{locale.DisplayName} ({locale.Code})",
            details: $"Removed {pages} page and {entries} entry translation(s)", cancellationToken: ct);
        return null;
    }
}

using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DotNetForge.Web.Services.Localization;

/// <summary>A locale as requests use it.</summary>
public sealed record LocaleInfo(Guid Id, string Code, string DisplayName, bool IsDefault, bool Enabled);

/// <summary>The site's locales at one moment: all of them, the default, and the enabled ones in their order.</summary>
public sealed class LocaleSet
{
    public LocaleSet(IReadOnlyList<LocaleInfo> all)
    {
        All = all;
        // The seeder guarantees a default; an empty table (a test database mid-reset) still answers with English.
        Default = all.FirstOrDefault(l => l.IsDefault) ?? new LocaleInfo(Guid.Empty, "en", "English", true, true);
        Enabled = all.Where(l => l.Enabled || l.IsDefault).ToList();
    }

    public IReadOnlyList<LocaleInfo> All { get; }
    public LocaleInfo Default { get; }
    public IReadOnlyList<LocaleInfo> Enabled { get; }

    /// <summary>An enabled locale by code (case-insensitive), else null.</summary>
    public LocaleInfo? FindEnabled(string? code) =>
        string.IsNullOrEmpty(code) ? null : Enabled.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>The enabled locales other than the default: the ones reached under a path prefix (<c>/de/...</c>).</summary>
    public IEnumerable<LocaleInfo> Translations => Enabled.Where(l => !l.IsDefault);
}

/// <summary>
/// The locales every request needs (path prefixes, the default, the switcher), cached for 30 s and cleared by
/// <see cref="LocaleService"/> on every change in this instance (.docs/features/internationalization.md). Singleton.
/// </summary>
public sealed class LocaleCatalog
{
    private const string CacheKey = "dnf:locales";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;

    public LocaleCatalog(IServiceScopeFactory scopes, IMemoryCache cache)
    {
        _scopes = scopes;
        _cache = cache;
    }

    public async Task<LocaleSet> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out LocaleSet? cached) && cached is not null)
        {
            return cached;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var rows = await db.Locales.AsNoTracking().OrderByDescending(l => l.IsDefault).ThenBy(l => l.SortOrder).ThenBy(l => l.Code)
            .Select(l => new LocaleInfo(l.Id, l.Code, l.DisplayName, l.IsDefault, l.Enabled))
            .ToListAsync(ct);
        var set = new LocaleSet(rows);
        _cache.Set(CacheKey, set, Lifetime);
        return set;
    }

    /// <summary>Forgets the cached locales (after a change); the next request reads them again.</summary>
    public void Invalidate() => _cache.Remove(CacheKey);
}

/// <summary>
/// Moves content when another locale becomes the default (<see cref="LocaleService.SetDefaultAsync"/>): the new default's
/// translations become the main values and the old main values become the old default's translations, inside the same
/// transaction. One implementation per kind of translated content (pages, entries).
/// </summary>
public interface IDefaultLocaleSwap
{
    Task SwapAsync(Locale oldDefault, Locale newDefault, CancellationToken ct);
}

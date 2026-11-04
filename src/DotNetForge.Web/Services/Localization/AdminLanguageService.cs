using System.Security.Claims;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DotNetForge.Web.Services.Localization;

/// <summary>
/// Which language the admin is shown in (.docs/features/internationalization.md#admin-language): the user's own choice on
/// Profile (<see cref="User.AdminLanguage"/>), else the site's admin language from Internationalization (the
/// <c>i18n.adminLanguage</c> setting), else English. Only languages with a catalog count (<see cref="AdminTranslations"/>);
/// a stored code whose catalog is gone falls through. Both values are cached for 30 s and cleared here on every change.
/// </summary>
public sealed class AdminLanguageService
{
    public const string SettingKey = "i18n.adminLanguage";

    private const string SiteCacheKey = "dnf:admin-language";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly IMemoryCache _cache;
    private readonly AdminTranslations _translations;

    public AdminLanguageService(DotNetForgeDbContext db, IAuditService audit, IMemoryCache cache, AdminTranslations translations)
    {
        _db = db;
        _audit = audit;
        _cache = cache;
        _translations = translations;
    }

    /// <summary>The languages the admin can be shown in (English first).</summary>
    public IReadOnlyList<AdminLanguageOption> Languages => _translations.Languages;

    /// <summary>The site's admin language: the setting when it names an offered language, else English.</summary>
    public async Task<AdminLanguageOption> SiteLanguageAsync(CancellationToken ct = default)
    {
        var code = await _cache.GetOrCreateAsync(SiteCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return await _db.Settings.AsNoTracking().Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        });
        return _translations.Language(code) ?? _translations.Languages[0];
    }

    /// <summary>The user's own choice, when it names an offered language; null = the site's admin language.</summary>
    public async Task<AdminLanguageOption?> UserLanguageAsync(Guid userId, CancellationToken ct = default)
    {
        var code = await _cache.GetOrCreateAsync(UserCacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.AdminLanguage).FirstOrDefaultAsync(ct);
        });
        return _translations.Language(code);
    }

    /// <summary>The language for a signed-in user (or the site's, for anyone else).</summary>
    public async Task<AdminLanguageOption> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && await UserLanguageAsync(id, ct) is { } own)
        {
            return own;
        }

        return await SiteLanguageAsync(ct);
    }

    /// <summary>Sets the site's admin language; returns an error message, or null when saved (and audited).</summary>
    public async Task<string?> SetSiteLanguageAsync(string? code, CancellationToken ct = default)
    {
        if (_translations.Language(code) is not { } language)
        {
            return "Choose one of the offered admin languages.";
        }

        var row = await _db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (row is null)
        {
            _db.Settings.Add(new Setting { Key = SettingKey, Value = language.Code });
        }
        else
        {
            row.Value = language.Code;
        }

        await _db.SaveChangesAsync(ct);
        _cache.Remove(SiteCacheKey);
        await _audit.LogAsync(AuditActions.AdminLanguageChanged, "Setting", SettingKey, "Admin language",
            details: $"{language.Name} ({language.Code})", cancellationToken: ct);
        return null;
    }

    /// <summary>
    /// Sets a user's own admin language (null or empty = follow the site's); returns an error message, or null when saved
    /// (and audited).
    /// </summary>
    public async Task<string?> SetUserLanguageAsync(Guid userId, string? code, CancellationToken ct = default)
    {
        AdminLanguageOption? language = null;
        if (!string.IsNullOrWhiteSpace(code) && (language = _translations.Language(code)) is null)
        {
            return "Choose one of the offered admin languages.";
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return "The account no longer exists.";
        }

        user.AdminLanguage = language?.Code;
        await _db.SaveChangesAsync(ct);
        _cache.Remove(UserCacheKey(userId));
        await _audit.LogAsync(AuditActions.AdminLanguageChanged, "User", userId.ToString(), user.DisplayName,
            details: language is null ? "Site default" : $"{language.Name} ({language.Code})", cancellationToken: ct);
        return null;
    }

    private static string UserCacheKey(Guid userId) => "dnf:admin-language:" + userId.ToString("N");
}

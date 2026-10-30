using System.Security.Claims;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DotNetForge.Web.Services;

/// <summary>
/// Server-side sign-in sessions (.docs/features/authentication.md#sessions). The cookie says who and which session; this
/// says whether that session still counts and which roles the user has now, so nothing in the cookie decides what they
/// may do. One query per user at most every <see cref="CacheLifetime"/>, dropped at once by a change on this instance; a
/// change on another instance arrives within that time. What the roles may do comes from <c>RoleGrantStore</c>.
/// </summary>
public sealed class UserSessionService
{
    /// <summary>The claim in the cookie that names the session.</summary>
    public const string SessionIdClaim = "dnf:sid";

    /// <summary>The longest a session lasts; a sliding cookie can't keep one alive past this.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly DotNetForgeDbContext _db;
    private readonly IMemoryCache _cache;

    public UserSessionService(DotNetForgeDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private sealed record CachedUser(IReadOnlyList<string> Roles, IReadOnlyDictionary<Guid, DateTime> Sessions);

    private static string CacheKey(Guid userId) => $"dnf:session:{userId}";

    /// <summary>Starts a session, and clears out the ones that have run out.</summary>
    public async Task<Guid> StartAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _db.UserSessions.Where(s => s.ExpiresDate <= now).ExecuteDeleteAsync(ct);
        var session = new UserSession { UserId = userId, CreatedDate = now, ExpiresDate = now.Add(MaxLifetime) };
        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        Forget(userId);
        return session.Id;
    }

    /// <summary>The user's roles right now, or null when the session no longer counts: ended, expired, or the account disabled or deleted.</summary>
    public async Task<IReadOnlyList<string>?> RolesAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(CacheKey(userId), out CachedUser? user))
        {
            user = await LoadAsync(userId, ct);
            _cache.Set(CacheKey(userId), user, CacheLifetime);
        }

        return user!.Sessions.TryGetValue(sessionId, out var expires) && expires > DateTime.UtcNow ? user.Roles : null;
    }

    private async Task<CachedUser> LoadAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Status == UserStatus.Enabled)
            .Select(u => new
            {
                Roles = _db.UserRoles.Where(ur => ur.UserId == u.Id).Join(_db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name).ToList(),
                Sessions = _db.UserSessions.Where(s => s.UserId == u.Id).Select(s => new { s.Id, s.ExpiresDate }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        return user is null
            ? new CachedUser(Array.Empty<string>(), new Dictionary<Guid, DateTime>())
            : new CachedUser(user.Roles, user.Sessions.ToDictionary(s => s.Id, s => s.ExpiresDate));
    }

    /// <summary>Signing out: this session ends here, so a copy of the cookie is worth nothing either.</summary>
    public async Task EndAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (Identify(user) is var (userId, sessionId))
        {
            await _db.UserSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct);
            Forget(userId);
        }
    }

    /// <summary>Ends every session of a user, for example when the account is disabled or its password reset.</summary>
    public async Task EndAllAsync(Guid userId, CancellationToken ct = default)
    {
        await _db.UserSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        Forget(userId);
    }

    /// <summary>Ends the user's other sessions and keeps this one: their own password change.</summary>
    public async Task EndOthersAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (Identify(user) is var (userId, sessionId))
        {
            await _db.UserSessions.Where(s => s.UserId == userId && s.Id != sessionId).ExecuteDeleteAsync(ct);
            Forget(userId);
        }
    }

    /// <summary>Their roles or status changed: the next request reads them again.</summary>
    public void Forget(Guid userId) => _cache.Remove(CacheKey(userId));

    /// <summary>The user and session a cookie names; null without them.</summary>
    public static (Guid UserId, Guid SessionId)? Identify(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
        && Guid.TryParse(user?.FindFirst(SessionIdClaim)?.Value, out var sessionId)
            ? (userId, sessionId)
            : null;
}

using System.Collections.Concurrent;
using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetForge.Data;

/// <summary>
/// <see cref="IRoleGrantStore"/> over the <c>Roles</c> and <c>RolePermissions</c> tables (two single-table queries, so
/// it runs on every provider). Grants are needed on almost every admin request and are read synchronously by
/// <c>PermissionService</c>, so each tenant's set is cached: dropped at once by <see cref="Invalidate"/> after a change
/// on this instance, and re-read at most <see cref="Lifetime"/> after a change made by another instance.
/// </summary>
public sealed class RoleGrantStore : IRoleGrantStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();
    private readonly ConcurrentDictionary<Guid, int> _versions = new();

    public RoleGrantStore(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public IReadOnlyDictionary<string, IReadOnlySet<string>> GetGrants(Guid tenantId)
    {
        if (_cache.TryGetValue(tenantId, out var entry) && entry.ExpiresUtc > DateTime.UtcNow)
        {
            return entry.Grants;
        }

        // Only cache a read that no Invalidate() overlapped, so a change can never be hidden by a stale read.
        var version = _versions.GetOrAdd(tenantId, 0);
        var grants = Load(tenantId);
        if (_versions.GetOrAdd(tenantId, 0) == version)
        {
            _cache[tenantId] = new Entry(grants, DateTime.UtcNow.Add(Lifetime));
        }

        return grants;
    }

    public void Invalidate(Guid tenantId)
    {
        _versions.AddOrUpdate(tenantId, 1, (_, v) => v + 1);
        _cache.TryRemove(tenantId, out _);
    }

    private IReadOnlyDictionary<string, IReadOnlySet<string>> Load(Guid tenantId)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var roles = db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => new { r.Id, r.Name })
            .ToList();
        var roleIds = roles.Select(r => r.Id).ToList();
        var permissions = db.RolePermissions.AsNoTracking()
            .Where(p => roleIds.Contains(p.RoleId))
            .Select(p => new { p.RoleId, p.Area, p.Action })
            .ToList();

        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            result[role.Name] = permissions
                .Where(p => p.RoleId == role.Id)
                .Select(p => PermissionCatalog.Key(p.Area, p.Action))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    private sealed record Entry(IReadOnlyDictionary<string, IReadOnlySet<string>> Grants, DateTime ExpiresUtc);
}

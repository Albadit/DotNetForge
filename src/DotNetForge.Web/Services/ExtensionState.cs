using DotNetForge.Data;
using DotNetForge.Sdk;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Which extensions are enabled: the <c>InstalledExtensions</c> rows with <c>Status = Enabled</c>
/// (.docs/features/extensions.md#enabled-state). Asked on almost every request (sidebar, gate, dashboard), so the set
/// is cached: refreshed at once after a change on this instance (<see cref="Invalidate"/>) and at most
/// <see cref="Lifetime"/> later when another instance changed it.
/// </summary>
public sealed class ExtensionState : IExtensionState
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private volatile Snapshot? _snapshot;
    private int _version;

    public ExtensionState(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async ValueTask<bool> IsEnabledAsync(string extensionId, CancellationToken cancellationToken = default) =>
        (await GetEnabledAsync(cancellationToken)).Contains(extensionId);

    public async ValueTask<IReadOnlySet<string>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshot is { } current && current.ExpiresUtc > DateTime.UtcNow)
        {
            return current.Enabled;
        }

        await _refresh.WaitAsync(cancellationToken);
        try
        {
            if (_snapshot is { } fresh && fresh.ExpiresUtc > DateTime.UtcNow)
            {
                return fresh.Enabled;
            }

            // Only cache a read that no Invalidate() overlapped, so a change can never be hidden by a stale read.
            var version = Volatile.Read(ref _version);
            var enabled = await LoadAsync(cancellationToken);
            if (Volatile.Read(ref _version) == version)
            {
                _snapshot = new Snapshot(enabled, DateTime.UtcNow.Add(Lifetime));
            }

            return enabled;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Drops the cached set; the next call reads the database.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _version);
        _snapshot = null;
    }

    private async Task<IReadOnlySet<string>> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var ids = await scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>().InstalledExtensions
            .AsNoTracking()
            .Where(x => x.Status == ExtensionStatus.Enabled)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Snapshot(IReadOnlySet<string> Enabled, DateTime ExpiresUtc);
}

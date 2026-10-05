using DotNetForge.Shared.Stores;

namespace DotNetForge.Web.Services;

/// <summary>
/// Caches the installed flag so the install-detection middleware doesn't hit the database on every
/// request. Installation is a one-way transition (uninstalled -&gt; installed), so once observed
/// true the value never reverts.
/// </summary>
public sealed class InstallationStatusCache
{
    private volatile bool _installed;

    public void MarkInstalled() => _installed = true;

    public async Task<bool> IsInstalledAsync(IInstallationStore store, CancellationToken cancellationToken = default)
    {
        if (_installed)
        {
            return true;
        }

        var installed = await store.IsInstalledAsync(cancellationToken);
        if (installed)
        {
            _installed = true;
        }

        return installed;
    }
}

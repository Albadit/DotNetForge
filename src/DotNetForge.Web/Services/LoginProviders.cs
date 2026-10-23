using DotNetForge.Extensions;
using DotNetForge.Sdk;

namespace DotNetForge.Web.Services;

/// <summary>
/// The external sign-in providers extensions offer (<c>AddLoginProvider</c>), filtered to the enabled ones
/// (.docs/features/authentication.md#external-sign-in-extensions).
/// </summary>
public sealed class LoginProviders
{
    private readonly IReadOnlyList<LoginProviderContribution> _contributions;
    private readonly ExtensionCatalog _catalog;

    public LoginProviders(IEnumerable<LoginProviderContribution> contributions, ExtensionCatalog catalog)
    {
        _contributions = contributions.ToList();
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<LoginProviderContribution>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var active = new List<LoginProviderContribution>();
        foreach (var provider in _contributions)
        {
            if (await _catalog.IsActiveAsync(provider.ExtensionId, cancellationToken))
            {
                active.Add(provider);
            }
        }

        return active;
    }

    /// <summary>The enabled provider for <paramref name="scheme"/> (case-insensitive), or <c>null</c>.</summary>
    public async Task<LoginProviderContribution?> FindActiveAsync(string scheme, CancellationToken cancellationToken = default) =>
        (await GetActiveAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Scheme, scheme, StringComparison.OrdinalIgnoreCase));
}

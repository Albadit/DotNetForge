using DotNetForge.Extensions;
using DotNetForge.Sdk;
using DotNetForge.Web.Authentication;

namespace DotNetForge.Web.Services;

/// <summary>A "Sign in with ..." button: a built-in provider (<see cref="ProviderKey"/>) or an extension's (<see cref="ExtensionId"/>).</summary>
public sealed record LoginOption(string Scheme, string DisplayName, string? ProviderKey, string? ExtensionId);

/// <summary>
/// The sign-in providers the sign-in screen offers (.docs/features/authentication.md#external-sign-in): the enabled,
/// configured built-in OAuth providers (Providers screen, <see cref="OAuthProviderRegistry"/>), then those of enabled
/// <c>authentication</c> extensions (<c>AddLoginProvider</c>).
/// </summary>
public sealed class LoginProviders
{
    private readonly IReadOnlyList<LoginProviderContribution> _contributions;
    private readonly ExtensionCatalog _catalog;
    private readonly OAuthProviderRegistry _registry;

    public LoginProviders(IEnumerable<LoginProviderContribution> contributions, ExtensionCatalog catalog, OAuthProviderRegistry registry)
    {
        _contributions = contributions.ToList();
        _catalog = catalog;
        _registry = registry;
    }

    /// <summary>Every provider extensions offer, enabled or not (listed read-only on the Providers screen).</summary>
    public IReadOnlyList<LoginProviderContribution> ExtensionProviders => _contributions;

    public async Task<IReadOnlyList<LoginOption>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var active = _registry.Active
            .Select(p => new LoginOption(p.Definition.Scheme, p.Definition.Name, p.Definition.Key, null))
            .ToList();
        foreach (var provider in _contributions)
        {
            if (await _catalog.IsActiveAsync(provider.ExtensionId, cancellationToken))
            {
                active.Add(new LoginOption(provider.Scheme, provider.DisplayName, null, provider.ExtensionId));
            }
        }

        return active;
    }

    /// <summary>The enabled provider for <paramref name="scheme"/> (case-insensitive), or <c>null</c>.</summary>
    public async Task<LoginOption?> FindActiveAsync(string scheme, CancellationToken cancellationToken = default) =>
        (await GetActiveAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Scheme, scheme, StringComparison.OrdinalIgnoreCase));
}

using System.Runtime.CompilerServices;
using DotNetForge.Core.Extensions;
using DotNetForge.Sdk;

namespace DotNetForge.Extensions;

/// <summary>An extension the host may use right now: valid, enabled, and - if it has code - loaded.</summary>
public sealed class ActiveExtension
{
    internal ActiveExtension(DiscoveredExtension discovered, ExtensionContext context, LoadedExtension? code)
    {
        Discovered = discovered;
        Context = context;
        Code = code;
    }

    public DiscoveredExtension Discovered { get; }

    /// <summary>The folder holding the manifest.</summary>
    public string Folder => Path.GetDirectoryName(Discovered.Path)!;

    public ExtensionContext Context { get; }

    /// <summary>The loaded code, or <c>null</c> for a views-only extension.</summary>
    public LoadedExtension? Code { get; }

    public string Id => Context.Id;

    /// <summary>The full path of <c>Views/&lt;relative&gt;</c> when that file exists, else <c>null</c>.</summary>
    public string? FindView(string relative)
    {
        var path = Path.Combine(Folder, "Views", relative);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>
/// The one place that answers "which extensions may contribute now?" (.docs/features/extensions.md): the live
/// discovery result (<see cref="IExtensionLoader"/>), the code loaded at startup (<see cref="ExtensionRuntime"/>) and
/// the enabled state from the Plugins screen (<see cref="IExtensionState"/>). Callers never combine these themselves.
/// </summary>
public sealed class ExtensionCatalog
{
    private readonly IExtensionLoader _loader;
    private readonly ExtensionRuntime _runtime;
    private readonly IExtensionState _state;
    private readonly Func<string, string?> _environment;
    private readonly ConditionalWeakTable<DiscoveredExtension, ExtensionContext> _contexts = new();

    public ExtensionCatalog(IExtensionLoader loader, ExtensionRuntime runtime, IExtensionState state, Func<string, string?> environment)
    {
        _loader = loader;
        _runtime = runtime;
        _state = state;
        _environment = environment;
    }

    /// <summary>The first valid manifest per id (the same rule everywhere), in discovery order.</summary>
    public IReadOnlyList<DiscoveredExtension> Installed() => FirstValidPerId(_loader.Discover());

    /// <summary>The rule behind <see cref="Installed"/>, for callers that already hold a discovery snapshot.</summary>
    public static IReadOnlyList<DiscoveredExtension> FirstValidPerId(IEnumerable<DiscoveredExtension> discovered) =>
        discovered
            .Where(d => d.IsValid)
            .GroupBy(d => d.Manifest!.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    /// <summary>
    /// Why a valid extension cannot run even if enabled - its code failed to load or appeared after startup - or
    /// <c>null</c> when it can.
    /// </summary>
    public string? Problem(DiscoveredExtension extension)
    {
        var manifest = extension.Manifest!;
        if (manifest.Assembly is null)
        {
            return null;
        }

        var code = _runtime.Find(manifest.Id!);
        if (code is null || !string.Equals(code.Discovered.Path, extension.Path, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(code.Manifest.Assembly, manifest.Assembly, StringComparison.OrdinalIgnoreCase))
        {
            return "Its code was added or changed after the app started - restart the app to load it.";
        }

        return code.Error;
    }

    /// <summary>Every active extension, optionally of one manifest <paramref name="type"/>, ordered by name.</summary>
    public async Task<IReadOnlyList<ActiveExtension>> GetActiveAsync(string? type = null, CancellationToken cancellationToken = default)
    {
        var enabled = await _state.GetEnabledAsync(cancellationToken);
        return Installed()
            .Where(d => enabled.Contains(d.Manifest!.Id!) &&
                        (type is null || string.Equals(d.Manifest.Type, type, StringComparison.OrdinalIgnoreCase)) &&
                        Problem(d) is null)
            .Select(ToActive)
            .OrderBy(a => a.Context.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The active extension with this id (and <paramref name="type"/>, when given), or <c>null</c>.</summary>
    public async Task<ActiveExtension?> FindActiveAsync(string id, string? type = null, CancellationToken cancellationToken = default)
    {
        var match = Installed().FirstOrDefault(d => string.Equals(d.Manifest!.Id, id, StringComparison.OrdinalIgnoreCase));
        if (match is null || (type is not null && !string.Equals(match.Manifest!.Type, type, StringComparison.OrdinalIgnoreCase)) ||
            Problem(match) is not null || !await _state.IsEnabledAsync(match.Manifest!.Id!, cancellationToken))
        {
            return null;
        }

        return ToActive(match);
    }

    /// <summary>Whether the extension with this id is active (valid, enabled, code loaded).</summary>
    public async Task<bool> IsActiveAsync(string id, CancellationToken cancellationToken = default) =>
        await FindActiveAsync(id, type: null, cancellationToken) is not null;

    private ActiveExtension ToActive(DiscoveredExtension discovered)
    {
        var code = discovered.Manifest!.Assembly is null ? null : _runtime.Find(discovered.Manifest.Id!);
        var context = code?.Context ?? _contexts.GetValue(discovered,
            d => new ExtensionContext(d.Manifest!, Path.GetDirectoryName(d.Path)!, _environment));
        return new ActiveExtension(discovered, context, code);
    }
}

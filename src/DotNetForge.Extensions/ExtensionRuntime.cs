using System.Reflection;
using DotNetForge.Core.Extensions;
using DotNetForge.Sdk;
using DotNetForge.Shared.Manifests;

namespace DotNetForge.Extensions;

/// <summary>One valid extension whose manifest names an <c>assembly</c>, and the outcome of loading its code.</summary>
public sealed class LoadedExtension
{
    private readonly List<string> _referencePaths = new();

    internal LoadedExtension(DiscoveredExtension discovered, ExtensionContext context)
    {
        Discovered = discovered;
        Context = context;
    }

    public DiscoveredExtension Discovered { get; }

    public ExtensionManifest Manifest => Discovered.Manifest!;

    public string Id => Context.Id;

    public ExtensionContext Context { get; }

    public Assembly? Assembly { get; private set; }

    /// <summary>The entry point, or <c>null</c> when loading failed (see <see cref="Error"/>).</summary>
    public IExtension? Instance { get; private set; }

    /// <summary>Why the code is not running; <c>null</c> when it loaded and configured without errors.</summary>
    public string? Error { get; private set; }

    public bool IsLoaded => Instance is not null && Error is null;

    /// <summary>Compile references for the extension's Razor views: its own assemblies and its dependencies'.</summary>
    public IReadOnlyList<string> ReferencePaths => _referencePaths;

    /// <summary>Marks the extension as failed (e.g. its <c>ConfigureServices</c> threw); it then contributes nothing.</summary>
    public void Fail(string error) => Error ??= error;

    internal void Loaded(Assembly assembly, IExtension instance, IEnumerable<string> referencePaths)
    {
        Assembly = assembly;
        Instance = instance;
        _referencePaths.AddRange(referencePaths.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Loads the code of every valid extension at startup, in dependency order, each into its own
/// <see cref="ExtensionLoadContext"/> (.docs/features/extensions.md#loading-code). A failure is recorded on the
/// extension (shown on the Plugins screen) and never stops the host. Code is loaded for enabled and disabled
/// extensions alike; the host decides at request time whether a contribution is used.
/// </summary>
public sealed class ExtensionRuntime
{
    private readonly Dictionary<string, LoadedExtension> _byId;

    private ExtensionRuntime(List<LoadedExtension> extensions)
    {
        Extensions = extensions;
        _byId = extensions.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Extensions with code, in load order (dependencies first).</summary>
    public IReadOnlyList<LoadedExtension> Extensions { get; }

    public LoadedExtension? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The extension that owns <paramref name="assembly"/>, if any.</summary>
    public LoadedExtension? FindByAssembly(Assembly assembly) => Extensions.FirstOrDefault(e => e.Assembly == assembly);

    /// <param name="discovered">The discovery result (<see cref="IExtensionLoader.Discover"/>).</param>
    /// <param name="environment">Deployment value lookup handed to every <see cref="ExtensionContext"/>.</param>
    public static ExtensionRuntime Load(IEnumerable<DiscoveredExtension> discovered, Func<string, string?> environment)
    {
        // Same rule as the rest of the host: the first valid manifest with an id wins.
        var valid = ExtensionCatalog.FirstValidPerId(discovered)
            .ToDictionary(d => d.Manifest!.Id!, StringComparer.OrdinalIgnoreCase);

        var order = new List<LoadedExtension>();
        var contexts = new Dictionary<string, ExtensionLoadContext?>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in valid.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            Visit(id);
        }

        return new ExtensionRuntime(order);

        // Depth-first so dependencies load before their dependents; returns the load context the dependents chain to.
        ExtensionLoadContext? Visit(string id)
        {
            if (contexts.TryGetValue(id, out var done))
            {
                return done;
            }

            var extension = valid[id];
            var manifest = extension.Manifest!;
            if (manifest.Assembly is null)
            {
                contexts[id] = null; // views only: nothing to load, nothing to share
                return null;
            }

            var folder = Path.GetDirectoryName(extension.Path)!;
            var loaded = new LoadedExtension(extension, new ExtensionContext(manifest, folder, environment));
            visiting.Add(id);

            var dependencyContexts = new List<ExtensionLoadContext>();
            var dependencyReferences = new List<string>();
            foreach (var dependencyId in manifest.Dependencies)
            {
                if (!valid.ContainsKey(dependencyId))
                {
                    loaded.Fail($"Depends on '{dependencyId}', which is not installed (or its manifest is invalid).");
                    continue;
                }

                if (visiting.Contains(dependencyId))
                {
                    loaded.Fail($"Circular dependency with '{dependencyId}'.");
                    continue;
                }

                var dependencyContext = Visit(dependencyId);
                var dependency = order.FirstOrDefault(o => string.Equals(o.Id, dependencyId, StringComparison.OrdinalIgnoreCase));
                if (dependency is { IsLoaded: false })
                {
                    loaded.Fail($"Depends on '{dependencyId}', whose code failed to load.");
                }
                else if (dependency is not null && dependencyContext is not null)
                {
                    dependencyContexts.Add(dependencyContext);
                    dependencyReferences.AddRange(dependency.ReferencePaths);
                }
            }

            visiting.Remove(id);
            var context = loaded.Error is null ? TryLoad(loaded, folder, dependencyContexts, dependencyReferences) : null;
            contexts[id] = context;
            order.Add(loaded);
            return context;
        }
    }

    private static ExtensionLoadContext? TryLoad(
        LoadedExtension loaded, string folder, IReadOnlyList<ExtensionLoadContext> dependencies, List<string> dependencyReferences)
    {
        var manifest = loaded.Manifest;
        var path = Path.GetFullPath(Path.Combine(folder, manifest.Assembly!));
        if (!File.Exists(path))
        {
            loaded.Fail($"'{manifest.Assembly}' was not found. Build the extension (dotnet build \"{folder}\") and restart the app.");
            return null;
        }

        // Extension code is foreign: whatever it throws while loading is reported on the extension, never rethrown.
        try
        {
            var context = new ExtensionLoadContext(loaded.Id, path, dependencies);
            var assembly = context.LoadMainAssembly();
            var entryType = FindEntryType(assembly, manifest.EntryPoint!);
            if (entryType is null)
            {
                loaded.Fail($"Entry point '{manifest.EntryPoint}' was not found in '{manifest.Assembly}'.");
                return null;
            }

            if (!typeof(IExtension).IsAssignableFrom(entryType) || entryType.IsAbstract ||
                entryType.GetConstructor(Type.EmptyTypes) is null)
            {
                loaded.Fail($"Entry point '{entryType.FullName}' must be a non-abstract class implementing " +
                            $"{typeof(IExtension).FullName} with a public parameterless constructor.");
                return null;
            }

            var instance = (IExtension)Activator.CreateInstance(entryType)!;
            loaded.Loaded(assembly, instance, context.PrivateAssemblyPaths().Concat(dependencyReferences));
            return context;
        }
        catch (Exception ex)
        {
            loaded.Fail($"Loading '{manifest.Assembly}' failed: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
            return null;
        }
    }

    /// <summary>The entry type by full name, or by simple name when the manifest omits the namespace.</summary>
    private static Type? FindEntryType(Assembly assembly, string entryPoint) =>
        assembly.GetType(entryPoint, throwOnError: false, ignoreCase: false) ??
        assembly.GetExportedTypes().SingleOrDefault(t => string.Equals(t.Name, entryPoint, StringComparison.Ordinal));
}

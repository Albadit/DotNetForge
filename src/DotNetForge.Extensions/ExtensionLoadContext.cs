using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace DotNetForge.Extensions;

/// <summary>
/// The isolated load context of one extension's code (.docs/features/extensions.md#loading-code). Resolution order for
/// every assembly the extension asks for:
/// <list type="number">
/// <item>an assembly the host already ships (the framework, <c>DotNetForge.*</c>, the host's NuGet packages) always
/// comes from the host, so contract types such as <c>IExtension</c> or <c>IFileStorage</c> are the same types on
/// both sides;</item>
/// <item>the extensions it declares in <c>dependencies</c> (libraries), so they share one copy;</item>
/// <item>its own build output (its <c>.deps.json</c>, else the file next to the main assembly).</item>
/// </list>
/// Assemblies are read into memory before loading, so the files are never locked: an extension can be rebuilt while the
/// app runs (the new code is picked up on the next start).
/// </summary>
/// <remarks>
/// Code outside every extension context can also bind to extension assemblies: views compiled at runtime (each loaded
/// into its own anonymous context) reference the extension types they use, so a failed resolution anywhere falls back
/// to the assemblies already loaded by extensions (<see cref="AppDomain.AssemblyResolve"/>).
/// </remarks>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private static readonly Lazy<HashSet<string>> HostAssemblies = new(FindHostAssemblies);
    private static readonly ConcurrentQueue<ExtensionLoadContext> Created = new();

    static ExtensionLoadContext()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => FindLoaded(new AssemblyName(args.Name));
    }

    private readonly string _mainAssemblyPath;
    private readonly string _directory;
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly IReadOnlyList<ExtensionLoadContext> _dependencies;
    private readonly Lock _gate = new();

    public ExtensionLoadContext(string extensionId, string mainAssemblyPath, IReadOnlyList<ExtensionLoadContext> dependencies)
        : base($"extension:{extensionId}")
    {
        _mainAssemblyPath = mainAssemblyPath;
        _directory = Path.GetDirectoryName(mainAssemblyPath)!;
        _dependencies = dependencies;
        Created.Enqueue(this);
        try
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }
        catch (InvalidOperationException)
        {
            _resolver = null; // unreadable .deps.json: fall back to probing the folder
        }
    }

    /// <summary>The extension's private assemblies on disk (not shared with the host); compile references for its views.</summary>
    public IEnumerable<string> PrivateAssemblyPaths() =>
        Directory.EnumerateFiles(_directory, "*.dll")
            .Where(path => !IsHostAssembly(Path.GetFileNameWithoutExtension(path)));

    public Assembly LoadMainAssembly()
    {
        lock (_gate)
        {
            return LoadBytes(_mainAssemblyPath);
        }
    }

    /// <summary>Whether the host provides an assembly with this simple name (then it is always shared).</summary>
    public static bool IsHostAssembly(string simpleName) => HostAssemblies.Value.Contains(simpleName);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null || IsHostAssembly(assemblyName.Name))
        {
            return null; // the default context resolves it: shared with the host
        }

        foreach (var dependency in _dependencies)
        {
            if (dependency.LoadOwn(assemblyName) is { } shared)
            {
                return shared;
            }
        }

        return LoadOwn(assemblyName);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path && File.Exists(path)
            ? LoadUnmanagedDllFromPath(path)
            : IntPtr.Zero;

    /// <summary>An assembly from this extension's own output (or its dependencies'), loaded at most once.</summary>
    private Assembly? LoadOwn(AssemblyName assemblyName)
    {
        lock (_gate)
        {
            var loaded = Assemblies.FirstOrDefault(a =>
                string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
            {
                return loaded;
            }

            var path = _resolver?.ResolveAssemblyToPath(assemblyName);
            if (path is null || !File.Exists(path))
            {
                path = Path.Combine(_directory, assemblyName.Name + ".dll");
            }

            if (File.Exists(path))
            {
                return LoadBytes(path);
            }
        }

        foreach (var dependency in _dependencies)
        {
            if (dependency.LoadOwn(assemblyName) is { } transitive)
            {
                return transitive;
            }
        }

        return null;
    }

    /// <summary>An assembly some extension context already loaded, newest context first.</summary>
    private static Assembly? FindLoaded(AssemblyName name) =>
        Created.Reverse()
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(a => string.Equals(a.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));

    private Assembly LoadBytes(string path)
    {
        using var image = new MemoryStream(File.ReadAllBytes(path));
        var pdbPath = Path.ChangeExtension(path, ".pdb");
        using var symbols = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
        return LoadFromStream(image, symbols);
    }

    /// <summary>
    /// Every assembly the host can load by itself: the trusted platform assemblies (framework + the app's own
    /// dependencies from its <c>.deps.json</c>), the files next to the app and anything already loaded.
    /// </summary>
    private static HashSet<string> FindHostAssemblies()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                names.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            names.Add(Path.GetFileNameWithoutExtension(path));
        }

        foreach (var assembly in Default.Assemblies)
        {
            if (assembly.GetName().Name is { } name)
            {
                names.Add(name);
            }
        }

        return names;
    }
}

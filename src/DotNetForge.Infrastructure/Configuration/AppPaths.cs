namespace DotNetForge.Infrastructure.Configuration;

/// <summary>
/// Locates repository-level folders for the two ways the app runs (.docs/architecture/codebase.md):
/// <list type="bullet">
/// <item>from a source checkout - the content root is <c>src/DotNetForge.Web</c>, while <c>.env</c>,
/// <c>extensions/</c> and the development <c>storage/</c> live at the repository root (the folder holding
/// <see cref="SolutionFileName"/>);</item>
/// <item>published - everything the app reads sits next to it in the content root (e.g. <c>/app/extensions</c>).</item>
/// </list>
/// </summary>
public static class AppPaths
{
    /// <summary>Marks the repository root.</summary>
    public const string SolutionFileName = "DotNetForge.slnx";

    /// <summary>The nearest ancestor of <paramref name="contentRoot"/> (inclusive) holding the solution file, or null.</summary>
    public static string? FindRepositoryRoot(string contentRoot)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(contentRoot)); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="name"/> in the content root when it exists there, else in the repository root when running
    /// from a checkout and it exists there; otherwise the content-root location (which may not exist).
    /// </summary>
    public static string Resolve(string contentRoot, string name)
    {
        var local = Path.Combine(contentRoot, name);
        if (File.Exists(local) || Directory.Exists(local))
        {
            return local;
        }

        var repositoryRoot = FindRepositoryRoot(contentRoot);
        if (repositoryRoot is not null)
        {
            var shared = Path.Combine(repositoryRoot, name);
            if (File.Exists(shared) || Directory.Exists(shared))
            {
                return shared;
            }
        }

        return local;
    }

    /// <summary>Where development-only defaults (SQLite file, local media) go: the repository root, else the content root.</summary>
    public static string DevelopmentDataRoot(string contentRoot) => FindRepositoryRoot(contentRoot) ?? contentRoot;
}

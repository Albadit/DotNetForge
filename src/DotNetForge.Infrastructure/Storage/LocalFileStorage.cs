using DotNetForge.Abstractions.Messaging;

namespace DotNetForge.Infrastructure.Storage;

/// <summary>
/// Default media storage provider writing under <c>storage/media</c> (file_manager.md). External
/// storage can be supplied later through connector extensions.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeFolder = SanitizeFolder(folder);
        var safeName = Path.GetFileName(fileName);
        var targetDir = Path.Combine(_root, safeFolder);
        Directory.CreateDirectory(targetDir);

        var fullPath = Path.Combine(targetDir, safeName);
        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
    }

    public Stream? OpenRead(string relativePath)
    {
        var fullPath = ResolveInsideRoot(relativePath);
        return fullPath is not null && File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
    }

    public bool Delete(string relativePath)
    {
        var fullPath = ResolveInsideRoot(relativePath);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return false;
        }

        File.Delete(fullPath);
        return true;
    }

    private static string SanitizeFolder(string folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? string.Empty
            : folder.Trim('/', '\\').Replace("..", string.Empty);

    private string? ResolveInsideRoot(string relativePath)
    {
        var rootFull = Path.GetFullPath(_root);
        var combined = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return combined.StartsWith(rootFull, StringComparison.Ordinal) ? combined : null;
    }
}

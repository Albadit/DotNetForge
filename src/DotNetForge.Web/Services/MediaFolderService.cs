using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Folders of the media library (.docs/pages/media.md#folders): create, rename, move and delete (empty only), and
/// moving files between them. Folders are virtual - a file's stored object never moves. Every folder keeps its
/// materialized <see cref="MediaFolder.Path"/> ("/Photos/2026", unique) in sync, so a rename or move updates the
/// folder and everything below it. Names are unique per parent, ignoring case.
/// </summary>
public sealed class MediaFolderService
{
    public const int MaxNameLength = 100;
    public const int MaxPathLength = 1000;

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;

    public MediaFolderService(DotNetForgeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>Every folder, ordered by path (parents before children) - libraries have few folders.</summary>
    public Task<List<MediaFolder>> AllAsync(CancellationToken ct = default) =>
        _db.MediaFolders.AsNoTracking().OrderBy(f => f.Path).ToListAsync(ct);

    public async Task<(MediaFolder? Folder, string? Error)> CreateAsync(Guid? parentId, string? name, Guid? userId, CancellationToken ct = default)
    {
        var (clean, error) = ValidateName(name);
        if (error is not null)
        {
            return (null, error);
        }

        var parent = parentId is { } id ? await _db.MediaFolders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct) : null;
        if (parentId is not null && parent is null)
        {
            return (null, "That folder no longer exists.");
        }

        var path = Combine(parent?.Path, clean!);
        if (await ConflictAsync(parentId, clean!, exceptId: null, ct) is { } conflict)
        {
            return (null, conflict);
        }

        if (path.Length > MaxPathLength)
        {
            return (null, "The folder is nested too deeply.");
        }

        var folder = new MediaFolder { Name = clean!, ParentId = parentId, Path = path, CreatedById = userId };
        _db.MediaFolders.Add(folder);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaFolderCreated, nameof(MediaFolder), folder.Id.ToString(), folder.Path, cancellationToken: ct);
        return (folder, null);
    }

    public async Task<string?> RenameAsync(Guid id, string? name, CancellationToken ct = default)
    {
        var folder = await _db.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null)
        {
            return "That folder no longer exists.";
        }

        var (clean, error) = ValidateName(name);
        if (error is not null)
        {
            return error;
        }

        if (clean == folder.Name)
        {
            return null;
        }

        if (await ConflictAsync(folder.ParentId, clean!, exceptId: id, ct) is { } conflict)
        {
            return conflict;
        }

        var parentPath = folder.ParentId is { } parentId
            ? await _db.MediaFolders.Where(f => f.Id == parentId).Select(f => f.Path).FirstAsync(ct)
            : null;
        var before = folder.Path;
        var pathError = await RepathAsync(folder, Combine(parentPath, clean!), ct);
        if (pathError is not null)
        {
            return pathError;
        }

        folder.Name = clean!;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaFolderUpdated, nameof(MediaFolder), id.ToString(), folder.Path,
            $"Renamed from {before}", cancellationToken: ct);
        return null;
    }

    /// <summary>Moves a folder (with everything in it) under <paramref name="parentId"/> (null = top level).</summary>
    public async Task<string?> MoveAsync(Guid id, Guid? parentId, CancellationToken ct = default)
    {
        var folder = await _db.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null)
        {
            return "That folder no longer exists.";
        }

        if (folder.ParentId == parentId)
        {
            return null;
        }

        var parent = parentId is { } target ? await _db.MediaFolders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == target, ct) : null;
        if (parentId is not null && parent is null)
        {
            return "The target folder no longer exists.";
        }

        if (parent is not null && (parent.Id == folder.Id || IsInside(parent.Path, folder.Path)))
        {
            return "A folder can't be moved into itself or into one of its own folders.";
        }

        if (await ConflictAsync(parentId, folder.Name, exceptId: id, ct) is { } conflict)
        {
            return conflict;
        }

        var before = folder.Path;
        var error = await RepathAsync(folder, Combine(parent?.Path, folder.Name), ct);
        if (error is not null)
        {
            return error;
        }

        folder.ParentId = parentId;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaFolderUpdated, nameof(MediaFolder), id.ToString(), folder.Path,
            $"Moved from {before}", cancellationToken: ct);
        return null;
    }

    /// <summary>Deletes an empty folder; one with files or folders in it is refused (nothing is deleted implicitly).</summary>
    public async Task<string?> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await _db.MediaFolders.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null)
        {
            return "That folder no longer exists.";
        }

        var files = await _db.MediaFiles.CountAsync(m => m.FolderId == id, ct);
        var folders = await _db.MediaFolders.CountAsync(f => f.ParentId == id, ct);
        if (files + folders > 0)
        {
            return $"'{folder.Name}' is not empty ({Count(files, "file")}{(files > 0 && folders > 0 ? ", " : "")}{Count(folders, "folder")}). Move or delete what is in it first.";
        }

        _db.MediaFolders.Remove(folder);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaFolderDeleted, nameof(MediaFolder), id.ToString(), folder.Path, cancellationToken: ct);
        return null;
    }

    /// <summary>Puts <paramref name="file"/> into <paramref name="folderId"/> (null = top level); the caller checked permissions.</summary>
    public async Task MoveFileAsync(MediaFile file, Guid? folderId, string? folderPath, CancellationToken ct = default)
    {
        if (file.FolderId == folderId)
        {
            return;
        }

        file.FolderId = folderId;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaMoved, nameof(MediaFile), file.Id.ToString(), file.FileName,
            $"To {folderPath ?? "/"}", cancellationToken: ct);
    }

    /// <summary>Whether <paramref name="path"/> lies below <paramref name="ancestor"/> ("/a/b" is inside "/a").</summary>
    public static bool IsInside(string path, string ancestor) =>
        path.StartsWith(ancestor + "/", StringComparison.Ordinal);

    /// <summary>A trimmed folder name, or why it can't be one.</summary>
    public static (string? Name, string? Error) ValidateName(string? name)
    {
        var clean = name?.Trim() ?? string.Empty;
        if (clean.Length == 0)
        {
            return (null, "Enter a folder name.");
        }

        if (clean.Length > MaxNameLength)
        {
            return (null, $"Folder names are limited to {MaxNameLength} characters.");
        }

        if (clean is "." or ".." || clean.Any(c => c is '/' or '\\' || char.IsControl(c)))
        {
            return (null, "Folder names can't contain / or \\ and can't be . or ..");
        }

        return (clean, null);
    }

    private static string Combine(string? parentPath, string name) => (parentPath ?? string.Empty) + "/" + name;

    private static string Count(int n, string noun) => n == 0 ? string.Empty : n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private async Task<string?> ConflictAsync(Guid? parentId, string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        var siblings = await _db.MediaFolders.AsNoTracking()
            .Where(f => f.ParentId == parentId && f.Id != exceptId)
            .Select(f => f.Name).ToListAsync(ct);
        return siblings.Any(s => s.ToLowerInvariant() == lower) ? $"There is already a folder '{name}' here." : null;
    }

    /// <summary>Gives <paramref name="folder"/> a new path and rewrites the paths of every folder below it (tracked, unsaved).</summary>
    private async Task<string?> RepathAsync(MediaFolder folder, string newPath, CancellationToken ct)
    {
        var oldPath = folder.Path;
        var prefix = oldPath + "/";
        var descendants = await _db.MediaFolders.Where(f => f.Path.StartsWith(prefix)).ToListAsync(ct);
        if (newPath.Length > MaxPathLength || descendants.Any(d => newPath.Length + d.Path.Length - oldPath.Length > MaxPathLength))
        {
            return "The folder would be nested too deeply.";
        }

        folder.Path = newPath;
        foreach (var descendant in descendants)
        {
            descendant.Path = newPath + descendant.Path[oldPath.Length..];
        }

        return null;
    }
}

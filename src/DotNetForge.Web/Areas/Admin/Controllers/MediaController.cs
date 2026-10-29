using System.IO.Compression;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Media library (.docs/pages/media.md): browse folders, upload, change visibility, move and delete. File bytes
/// live in <c>IFileStorage</c>; rules are in <see cref="MediaService"/> and <see cref="MediaFolderService"/> (folders
/// are virtual). Upload and creating folders need <c>Media/create</c>; changing public/private, moving and renaming
/// files need <c>Media/update</c> (or <c>update.own</c>); renaming and moving folders <c>Media/update</c>; delete needs
/// <c>Media/delete</c> (or <c>delete.own</c> for files) - the "own" grants for files the user uploaded.
/// </summary>
[Route("admin/media")]
[RequirePermission(PermissionAreas.Media, PermissionActions.Read)]
public sealed class MediaController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly MediaService _media;
    private readonly MediaFolderService _folders;
    private readonly IFileStorage _storage;

    public MediaController(DotNetForgeDbContext db, MediaService media, MediaFolderService folders, IFileStorage storage)
    {
        _db = db;
        _media = media;
        _folders = folders;
        _storage = storage;
    }

    /// <summary>Largest .zip whose contents the preview lists (it is read into memory: storage streams can't seek).</summary>
    public const long MaxZipListBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The file from this site's own origin, for the preview dialog (.docs/pages/media.md#preview): PDFs and text
    /// can't be framed from object storage (the Content-Security-Policy allows frames from this site only). Text and
    /// CSV are sent as plain text so they show instead of downloading; <paramref name="download"/> sends any file as an
    /// attachment. Reading the library is enough - private files included, like on the screen.
    /// </summary>
    [HttpGet("{id:guid}/inline")]
    public async Task<IActionResult> Inline(Guid id, bool download = false)
    {
        var file = await _db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (file is null)
        {
            return NotFound();
        }

        var stored = await _storage.OpenReadAsync(file.RelativePath, HttpContext.RequestAborted);
        if (stored is null)
        {
            return NotFound();
        }

        var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        Response.Headers.CacheControl = "private, no-store";
        var type = !download && file.ContentType.StartsWith("text/", StringComparison.Ordinal) ? "text/plain; charset=utf-8" : file.ContentType;
        return File(stored.Content, type, enableRangeProcessing: stored.Content.CanSeek);
    }

    /// <summary>What a .zip holds (the preview dialog lists it): the first 500 entries with their sizes.</summary>
    [HttpGet("{id:guid}/zip")]
    public async Task<IActionResult> ZipEntries(Guid id)
    {
        var file = await _db.MediaFiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (file is null || file.ContentType != "application/zip")
        {
            return NotFound();
        }

        if (file.SizeBytes > MaxZipListBytes)
        {
            return Json(new { error = $"Too large to list here (over {MaxZipListBytes / 1024 / 1024} MB). Download it instead." });
        }

        await using var stored = await _storage.OpenReadAsync(file.RelativePath, HttpContext.RequestAborted);
        if (stored is null)
        {
            return NotFound();
        }

        try
        {
            using var buffer = new MemoryStream();
            await stored.Content.CopyToAsync(buffer, HttpContext.RequestAborted);
            buffer.Position = 0;
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
            var entries = zip.Entries.Take(500).Select(e => new { name = e.FullName, size = e.Length, folder = e.FullName.EndsWith('/') }).ToList();
            return Json(new { entries, total = zip.Entries.Count });
        }
        catch (InvalidDataException)
        {
            return Json(new { error = "This .zip file is damaged or not a .zip file." });
        }
    }

    /// <summary>The library at the top level, or inside <paramref name="folder"/>.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid? folder)
    {
        if (folder is { } id && !await _db.MediaFolders.AnyAsync(f => f.Id == id))
        {
            return NotFound();
        }

        return View(await BuildIndexAsync(folder));
    }

    [HttpPost("folders")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(Guid? parentId, string? name)
    {
        if (!Can(PermissionAreas.Media, PermissionActions.Create))
        {
            return Forbid();
        }

        // CreateAsync writes the MediaFolderCreated audit entry.
        var (folder, error) = await _folders.CreateAsync(parentId, name, CurrentUserId, HttpContext.RequestAborted);
        if (folder is null)
        {
            TempData["Error"] = error;
            return Back(parentId);
        }

        TempData["Success"] = $"Created the folder '{folder.Name}'.";
        return Back(parentId);
    }

    [HttpPost("folders/{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameFolder(Guid id, string? name)
    {
        if (!Can(PermissionAreas.Media, PermissionActions.Update))
        {
            return Forbid();
        }

        // RenameAsync writes the MediaFolderUpdated audit entry.
        var error = await _folders.RenameAsync(id, name, HttpContext.RequestAborted);
        TempData[error is null ? "Success" : "Error"] = error ?? "Folder renamed.";
        return Back(id);
    }

    [HttpPost("folders/{id:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveFolder(Guid id, Guid? parentId)
    {
        if (!Can(PermissionAreas.Media, PermissionActions.Update))
        {
            return Forbid();
        }

        // MoveAsync writes the MediaFolderUpdated audit entry.
        var error = await _folders.MoveAsync(id, parentId, HttpContext.RequestAborted);
        TempData[error is null ? "Success" : "Error"] = error ?? "Folder moved.";
        return Back(id);
    }

    [HttpPost("folders/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(Guid id)
    {
        if (!Can(PermissionAreas.Media, PermissionActions.Delete))
        {
            return Forbid();
        }

        var parentId = await _db.MediaFolders.Where(f => f.Id == id).Select(f => f.ParentId).FirstOrDefaultAsync();
        // DeleteAsync writes the MediaFolderDeleted audit entry; a folder with content is refused.
        var error = await _folders.DeleteAsync(id, HttpContext.RequestAborted);
        if (error is not null)
        {
            TempData["Error"] = error;
            return Back(id);
        }

        TempData["Success"] = "Folder deleted.";
        return Back(parentId);
    }

    /// <summary>
    /// Moves the selected files and folders into <paramref name="folderId"/> (empty = top level): files with the same
    /// checks as visibility, folders with Media update and the folder rules (not into themselves or their subfolders).
    /// </summary>
    [HttpPost("move-selected")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveSelected(List<Guid> ids, List<Guid> folderIds, Guid? folderId, Guid? returnFolder)
    {
        ids = ids.Distinct().ToList();
        folderIds = folderIds.Distinct().ToList();
        if (ids.Count + folderIds.Count == 0 || ids.Count + folderIds.Count > MaxBulkDelete)
        {
            TempData["Error"] = ids.Count + folderIds.Count == 0 ? "Select at least one file or folder." : $"Move at most {MaxBulkDelete} items at a time.";
            return Back(returnFolder);
        }

        string? targetPath = null;
        if (folderId is { } target)
        {
            targetPath = await _db.MediaFolders.Where(f => f.Id == target).Select(f => f.Path).FirstOrDefaultAsync();
            if (targetPath is null)
            {
                TempData["Error"] = "The target folder no longer exists.";
                return Back(returnFolder);
            }
        }

        var files = await _db.MediaFiles.Where(m => ids.Contains(m.Id)).ToListAsync();
        var moved = 0;
        var refused = 0;
        foreach (var file in files)
        {
            if (!CanModify(PermissionAreas.Media, PermissionActions.Update, PermissionActions.UpdateOwn, file.UploadedById))
            {
                refused++;
                continue;
            }

            // MoveFileAsync writes a MediaMoved audit entry per file.
            await _folders.MoveFileAsync(file, folderId, targetPath, HttpContext.RequestAborted);
            moved++;
        }

        var problems = new List<string>();
        if (folderIds.Count > 0 && !Can(PermissionAreas.Media, PermissionActions.Update))
        {
            refused += folderIds.Count;
        }
        else
        {
            // Only folders that exist and aren't in the target already (nor are the target).
            var parents = await _db.MediaFolders.Where(f => folderIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.ParentId);
            foreach (var id in folderIds.Where(id => id != folderId && parents.TryGetValue(id, out var parent) && parent != folderId))
            {
                // MoveAsync writes the MediaFolderUpdated audit entry; it refuses a move into the folder itself or below.
                if (await _folders.MoveAsync(id, folderId, HttpContext.RequestAborted) is { } problem)
                {
                    problems.Add(problem);
                }
                else
                {
                    moved++;
                }
            }
        }

        if (moved > 0)
        {
            TempData["Success"] = $"Moved {Items(moved)} to {targetPath ?? "the top level"}.";
        }

        if (refused > 0 || problems.Count > 0)
        {
            TempData["Error"] = string.Join(" ", new[]
            {
                refused > 0 ? $"{Items(refused)} not moved: your role may not change them." : null,
                problems.Count > 0 ? problems[0] + (problems.Count > 1 ? $" (and {problems.Count - 1} more)" : null) : null,
            }.OfType<string>());
        }

        return Back(returnFolder);
    }

    private static string Items(int n) => n == 1 ? "1 item" : $"{n} items";

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [MediaUploadLimit(Files = MediaService.MaxFilesPerUpload)]
    public async Task<IActionResult> Upload(List<IFormFile> files, bool isPublic, Guid? folderId)
    {
        if (!Can(PermissionAreas.Media, PermissionActions.Create))
        {
            return Forbid();
        }

        if (files.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose at least one file to upload.");
        }
        else if (files.Count > MediaService.MaxFilesPerUpload)
        {
            ModelState.AddModelError(string.Empty, $"Upload at most {MediaService.MaxFilesPerUpload} files at a time.");
        }

        if (folderId is { } target && !await _db.MediaFolders.AnyAsync(f => f.Id == target))
        {
            ModelState.AddModelError(string.Empty, "That folder no longer exists.");
            folderId = null;
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildIndexAsync(folderId));
        }

        var uploaded = 0;
        foreach (var file in files)
        {
            var (saved, error) = await _media.UploadAsync(file, isPublic, CurrentUserId, folderId, ct: HttpContext.RequestAborted);
            if (error is not null)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            else if (saved is not null)
            {
                uploaded++;
            }
        }

        if (!ModelState.IsValid)
        {
            // Some files failed: show which, alongside the ones that were stored.
            return View(nameof(Index), await BuildIndexAsync(folderId));
        }

        TempData["Success"] = uploaded == 1 ? "Uploaded 1 file." : $"Uploaded {uploaded} files.";
        return Back(folderId);
    }

    [HttpPost("visibility/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Visibility(Guid id, bool isPublic)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id);
        if (file is null)
        {
            return NotFound();
        }

        if (!CanModify(PermissionAreas.Media, PermissionActions.Update, PermissionActions.UpdateOwn, file.UploadedById))
        {
            return Forbid();
        }

        await _media.SetVisibilityAsync(file, isPublic, HttpContext.RequestAborted);
        TempData["Success"] = isPublic
            ? $"'{file.FileName}' is now public: anyone with the link can open it."
            : $"'{file.FileName}' is now private: only signed-in admin users can open it.";
        return Back(file.FolderId);
    }

    [HttpPost("{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(Guid id, string? name)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id);
        if (file is null)
        {
            return NotFound();
        }

        if (!CanModify(PermissionAreas.Media, PermissionActions.Update, PermissionActions.UpdateOwn, file.UploadedById))
        {
            return Forbid();
        }

        var before = file.FileName;
        // RenameAsync writes the MediaUpdated audit entry.
        var error = await _media.RenameAsync(file, name, HttpContext.RequestAborted);
        TempData[error is null ? "Success" : "Error"] = error ?? (file.FileName == before ? "The name is unchanged." : $"Renamed to '{file.FileName}'.");
        return Back(file.FolderId);
    }

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id);
        if (file is null)
        {
            return NotFound();
        }

        if (!CanModify(PermissionAreas.Media, PermissionActions.Delete, PermissionActions.DeleteOwn, file.UploadedById))
        {
            return Forbid();
        }

        await _media.DeleteAsync(file, HttpContext.RequestAborted);
        TempData["Success"] = $"Deleted '{file.FileName}'.";
        return Back(file.FolderId);
    }

    /// <summary>Most files one bulk delete accepts.</summary>
    public const int MaxBulkDelete = 100;

    /// <summary>
    /// Deletes the selected files (the library's bulk action). Each file gets the same permission check as
    /// <see cref="Delete"/> (and its own audit entry through <see cref="MediaService"/>); files the user may not
    /// delete are skipped and reported.
    /// </summary>
    [HttpPost("delete-selected")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(List<Guid> ids, List<Guid> folderIds, Guid? returnFolder)
    {
        ids = ids.Distinct().ToList();
        folderIds = folderIds.Distinct().ToList();
        if (ids.Count + folderIds.Count == 0 || ids.Count + folderIds.Count > MaxBulkDelete)
        {
            TempData["Error"] = ids.Count + folderIds.Count == 0 ? "Select at least one file or folder." : $"Delete at most {MaxBulkDelete} items at a time.";
            return Back(returnFolder);
        }

        var files = await _db.MediaFiles.Where(m => ids.Contains(m.Id)).ToListAsync();
        var deleted = 0;
        var refused = 0;
        foreach (var file in files)
        {
            if (!CanModify(PermissionAreas.Media, PermissionActions.Delete, PermissionActions.DeleteOwn, file.UploadedById))
            {
                refused++;
                continue;
            }

            await _media.DeleteAsync(file, HttpContext.RequestAborted);
            deleted++;
        }

        // Folders after the files, so a folder whose files were selected too can go now that it is empty.
        var notEmpty = 0;
        if (folderIds.Count > 0 && !Can(PermissionAreas.Media, PermissionActions.Delete))
        {
            refused += folderIds.Count;
        }
        else
        {
            var existing = await _db.MediaFolders.Where(f => folderIds.Contains(f.Id)).Select(f => f.Id).ToListAsync();
            foreach (var id in existing)
            {
                // DeleteAsync writes the MediaFolderDeleted audit entry; a folder with content is refused.
                if (await _folders.DeleteAsync(id, HttpContext.RequestAborted) is null)
                {
                    deleted++;
                }
                else
                {
                    notEmpty++;
                }
            }
        }

        if (deleted > 0)
        {
            TempData["Success"] = $"Deleted {Items(deleted)}.";
        }

        if (refused > 0 || notEmpty > 0)
        {
            TempData["Error"] = string.Join(" ", new[]
            {
                refused > 0 ? $"{Items(refused)} not deleted: your role may not delete them." : null,
                notEmpty > 0 ? $"{(notEmpty == 1 ? "1 folder was" : $"{notEmpty} folders were")} not deleted: only empty folders can be deleted." : null,
            }.OfType<string>());
        }
        else if (deleted == 0)
        {
            TempData["Error"] = "None of the selected items exist any more.";
        }

        return Back(returnFolder);
    }

    /// <summary>Back to the library, in <paramref name="folder"/> when there is one.</summary>
    private RedirectResult Back(Guid? folder) => Redirect(folder is { } id ? $"/admin/media?folder={id}" : "/admin/media");

    private async Task<MediaIndexViewModel> BuildIndexAsync(Guid? folderId)
    {
        var folders = await _folders.AllAsync(HttpContext.RequestAborted);
        var current = folderId is { } id ? folders.FirstOrDefault(f => f.Id == id) : null;
        var rows = await _db.MediaFiles
            .AsNoTracking()
            .Where(m => m.FolderId == folderId)
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new { m.Id, m.FileName, m.OriginalName, m.ContentType, m.SizeBytes, m.OriginalSizeBytes, m.Optimization, m.IsPublic, m.UploadedDate, m.UploadedById })
            .ToListAsync();
        var uploaderIds = rows.Where(m => m.UploadedById is not null).Select(m => m.UploadedById!.Value).Distinct().ToList();
        var uploaders = await _db.Users.AsNoTracking().Where(u => uploaderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email);

        // Content counts of the subfolders shown here (single-table queries, combined in memory).
        var children = folders.Where(f => f.ParentId == folderId).ToList();
        var childIds = children.Select(f => f.Id).ToList();
        var fileCounts = await _db.MediaFiles.AsNoTracking()
            .Where(m => m.FolderId != null && childIds.Contains(m.FolderId.Value))
            .GroupBy(m => m.FolderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key!.Value, g => g.Count);

        var canDeleteAny = Can(PermissionAreas.Media, PermissionActions.Delete);
        var canDeleteOwn = Can(PermissionAreas.Media, PermissionActions.DeleteOwn);
        var canUpdateAny = Can(PermissionAreas.Media, PermissionActions.Update);
        var canUpdateOwn = Can(PermissionAreas.Media, PermissionActions.UpdateOwn);

        var breadcrumbs = new List<MediaFolderLink>();
        for (var f = current; f is not null; f = f.ParentId is { } parent ? folders.FirstOrDefault(x => x.Id == parent) : null)
        {
            breadcrumbs.Insert(0, new MediaFolderLink(f.Id, f.Name, f.Path));
        }

        return new MediaIndexViewModel
        {
            CanUpload = Can(PermissionAreas.Media, PermissionActions.Create),
            Limits = await _media.SettingsAsync(HttpContext.RequestAborted),
            CanEditFolders = canUpdateAny,
            CanDeleteFolders = canDeleteAny,
            Current = current is null ? null : new MediaFolderLink(current.Id, current.Name, current.Path),
            Breadcrumbs = breadcrumbs,
            Folders = children.Select(f => new MediaFolderRowViewModel
            {
                Id = f.Id,
                Name = f.Name,
                Path = f.Path,
                Files = fileCounts.GetValueOrDefault(f.Id),
                Folders = folders.Count(x => x.ParentId == f.Id),
            }).ToList(),
            AllFolders = folders.Select(f => new MediaFolderLink(f.Id, f.Name, f.Path)).ToList(),
            Files = rows.Select(m => new MediaRowViewModel
            {
                Id = m.Id,
                FileName = m.FileName,
                OriginalName = m.OriginalName,
                ContentType = m.ContentType,
                SizeBytes = m.SizeBytes,
                OriginalSizeBytes = m.OriginalSizeBytes,
                Optimization = m.Optimization,
                IsPublic = m.IsPublic,
                UploadedDate = m.UploadedDate,
                UploadedBy = m.UploadedById is { } by ? uploaders.GetValueOrDefault(by) : null,
                Url = MediaService.UrlFor(m.Id, m.FileName),
                CanDelete = canDeleteAny || (canDeleteOwn && m.UploadedById is not null && m.UploadedById == CurrentUserId),
                CanEdit = canUpdateAny || (canUpdateOwn && m.UploadedById is not null && m.UploadedById == CurrentUserId),
            }).ToList(),
        };
    }
}

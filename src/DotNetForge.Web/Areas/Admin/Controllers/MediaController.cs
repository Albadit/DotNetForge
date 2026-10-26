using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Media library (.docs/pages/media.md): list, upload, change visibility and delete. File bytes
/// live in <c>IFileStorage</c>; rules are in <see cref="MediaService"/>. Upload needs <c>Media/create</c>; changing
/// public/private needs <c>Media/update</c> (or <c>update.own</c>); delete needs <c>Media/delete</c> (or
/// <c>delete.own</c>) - the "own" grants for files the user uploaded.
/// </summary>
[Route("admin/media")]
[RequirePermission(PermissionAreas.Media, PermissionActions.Read)]
public sealed class MediaController : AdminControllerBase
{
    /// <summary>Request cap: the per-file limit times the file count, plus room for the multipart envelope.</summary>
    private const long MaxRequestBytes = MediaService.MaxUploadBytes * MediaService.MaxFilesPerUpload + 1024 * 1024;

    private readonly DotNetForgeDbContext _db;
    private readonly MediaService _media;

    public MediaController(DotNetForgeDbContext db, MediaService media)
    {
        _db = db;
        _media = media;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await BuildIndexAsync());

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Upload(List<IFormFile> files, bool isPublic)
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

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildIndexAsync());
        }

        var uploaded = 0;
        foreach (var file in files)
        {
            var (saved, error) = await _media.UploadAsync(file, isPublic, CurrentUserId, HttpContext.RequestAborted);
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
            return View(nameof(Index), await BuildIndexAsync());
        }

        TempData["Success"] = uploaded == 1 ? "Uploaded 1 file." : $"Uploaded {uploaded} files.";
        return RedirectToAction(nameof(Index));
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
        return RedirectToAction(nameof(Index));
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
        return RedirectToAction(nameof(Index));
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
    public async Task<IActionResult> DeleteSelected(List<Guid> ids)
    {
        ids = ids.Distinct().ToList();
        if (ids.Count == 0 || ids.Count > MaxBulkDelete)
        {
            TempData["Error"] = ids.Count == 0 ? "Select at least one file." : $"Delete at most {MaxBulkDelete} files at a time.";
            return RedirectToAction(nameof(Index));
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

        if (deleted > 0)
        {
            TempData["Success"] = deleted == 1 ? "Deleted 1 file." : $"Deleted {deleted} files.";
        }

        if (refused > 0)
        {
            TempData["Error"] = $"{refused} {(refused == 1 ? "file was" : "files were")} not deleted: your role may not delete them.";
        }
        else if (deleted == 0)
        {
            TempData["Error"] = "None of the selected files exist any more.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<MediaIndexViewModel> BuildIndexAsync()
    {
        var rows = await _db.MediaFiles
            .AsNoTracking()
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new { m.Id, m.FileName, m.ContentType, m.SizeBytes, m.IsPublic, m.UploadedDate, m.UploadedById })
            .ToListAsync();

        var canDeleteAny = Can(PermissionAreas.Media, PermissionActions.Delete);
        var canDeleteOwn = Can(PermissionAreas.Media, PermissionActions.DeleteOwn);
        var canUpdateAny = Can(PermissionAreas.Media, PermissionActions.Update);
        var canUpdateOwn = Can(PermissionAreas.Media, PermissionActions.UpdateOwn);

        return new MediaIndexViewModel
        {
            CanUpload = Can(PermissionAreas.Media, PermissionActions.Create),
            Files = rows.Select(m => new MediaRowViewModel
            {
                Id = m.Id,
                FileName = m.FileName,
                ContentType = m.ContentType,
                SizeBytes = m.SizeBytes,
                IsPublic = m.IsPublic,
                UploadedDate = m.UploadedDate,
                Url = MediaService.UrlFor(m.Id, m.FileName),
                CanDelete = canDeleteAny || (canDeleteOwn && m.UploadedById is not null && m.UploadedById == CurrentUserId),
                CanChangeVisibility = canUpdateAny || (canUpdateOwn && m.UploadedById is not null && m.UploadedById == CurrentUserId),
            }).ToList(),
        };
    }
}

using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Media library for the active tenant (.docs/pages/media.md): list, upload and delete. File bytes live in
/// <c>IFileStorage</c>; rules are in <see cref="MediaService"/>. Upload needs <c>Media/create</c>; delete needs
/// <c>Media/delete</c>, or <c>Media/delete.own</c> for files the user uploaded.
/// </summary>
[Route("admin/media")]
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
            var (saved, error) = await _media.UploadAsync(file, isPublic, TenantId, CurrentUserId, HttpContext.RequestAborted);
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

    [HttpPost("delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await _db.MediaFiles.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == TenantId);
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

    private async Task<MediaIndexViewModel> BuildIndexAsync()
    {
        var rows = await _db.MediaFiles
            .AsNoTracking()
            .Where(m => m.TenantId == TenantId)
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new { m.Id, m.FileName, m.ContentType, m.SizeBytes, m.IsPublic, m.UploadedDate, m.UploadedById })
            .ToListAsync();

        var canDeleteAny = Can(PermissionAreas.Media, PermissionActions.Delete);
        var canDeleteOwn = Can(PermissionAreas.Media, PermissionActions.DeleteOwn);

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
            }).ToList(),
        };
    }
}

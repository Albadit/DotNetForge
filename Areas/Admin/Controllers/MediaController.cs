using DotNetForge.Data;
using DotNetForge.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>Read-only media library list for the active tenant (file_manager.md).</summary>
[Route("admin/media")]
public sealed class MediaController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public MediaController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var media = await _db.MediaFiles
            .AsNoTracking()
            .Where(m => m.TenantId == TenantId)
            .OrderByDescending(m => m.UploadedDate)
            .Select(m => new MediaRowViewModel
            {
                Id = m.Id,
                FileName = m.FileName,
                ContentType = m.ContentType,
                SizeBytes = m.SizeBytes,
                IsPublic = m.IsPublic,
                UploadedDate = m.UploadedDate,
            })
            .ToListAsync();

        return View(media);
    }
}

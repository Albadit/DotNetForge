using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Serves media files at <c>/media/{id}/{fileName}</c> (.docs/features/media-storage.md). Public files are open to
/// everyone; private files only to admin-capable users of the file's tenant - anyone else gets 404, so a private
/// file's existence is not revealed. After authorization the browser is redirected to a short-lived presigned URL
/// (S3 providers) or the bytes are streamed by the app (local provider).
/// </summary>
[Route("media")]
public sealed class MediaFilesController : Controller
{
    public static readonly TimeSpan PublicUrlLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan PrivateUrlLifetime = TimeSpan.FromMinutes(5);

    private readonly DotNetForgeDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<MediaFilesController> _logger;

    public MediaFilesController(DotNetForgeDbContext db, IFileStorage storage, ILogger<MediaFilesController> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>The trailing file name only makes URLs readable; the id identifies the file.</summary>
    [HttpGet("{id:guid}/{fileName?}")]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await _db.MediaFiles.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.TenantId, m.FileName, m.ContentType, m.RelativePath, m.IsPublic })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (file is null || (!file.IsPublic && !CanReadPrivate(file.TenantId)))
        {
            return NotFound();
        }

        var disposition = new ContentDispositionHeaderValue(MediaService.IsInline(file.ContentType) ? "inline" : "attachment");
        disposition.SetHttpFileName(file.FileName);

        Response.Headers.CacheControl = file.IsPublic ? "public, max-age=300" : "private, no-store";

        var url = await _storage.GetDownloadUrlAsync(file.RelativePath, disposition.ToString(),
            file.IsPublic ? PublicUrlLifetime : PrivateUrlLifetime, HttpContext.RequestAborted);
        if (url is not null)
        {
            // AbsoluteUri keeps the percent-encoding of the signed query (ToString() would unescape it).
            return Redirect(url.AbsoluteUri);
        }

        var stored = await _storage.OpenReadAsync(file.RelativePath, HttpContext.RequestAborted);
        if (stored is null)
        {
            _logger.LogWarning("Media file {MediaId} has no stored object at {StorageKey}.", id, file.RelativePath);
            return NotFound();
        }

        Response.Headers.ContentDisposition = disposition.ToString();
        // FileStreamResult disposes the stream (and with it the provider's connection) after the response.
        return File(stored.Content, file.ContentType, enableRangeProcessing: stored.Content.CanSeek);
    }

    private bool CanReadPrivate(Guid fileTenantId) =>
        User.Identity?.IsAuthenticated == true &&
        Roles.AdminCapable.Any(User.IsInRole) &&
        Guid.TryParse(User.FindFirst(AuthService.TenantClaimType)?.Value, out var tenant) &&
        tenant == fileTenantId;
}

using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Media Library settings (.docs/pages/media-settings.md, .docs/features/media-storage.md#optimization): upload limits per
/// kind of file, image optimization and video compression, plus what the library uses in storage. Reading needs
/// Settings read, saving Settings update; the rules live in <see cref="MediaSettingsService"/>.
/// </summary>
[Route("admin/media-settings")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class MediaSettingsController : AdminControllerBase
{
    private readonly MediaSettingsService _settings;
    private readonly Ffmpeg _ffmpeg;
    private readonly DotNetForgeDbContext _db;

    public MediaSettingsController(MediaSettingsService settings, Ffmpeg ffmpeg, DotNetForgeDbContext db)
    {
        _settings = settings;
        _ffmpeg = ffmpeg;
        _db = db;
    }

    /// <summary>The form (bound on save) and what the screen shows around it.</summary>
    public sealed class MediaSettingsViewModel
    {
        public int MaxImageMb { get; set; }
        public int MaxVideoMb { get; set; }
        public int MaxOtherMb { get; set; }
        public bool OptimizeImages { get; set; }
        public int ImageMaxWidth { get; set; }
        public int ImageMaxHeight { get; set; }
        public int ImageQuality { get; set; }
        public bool ConvertToWebP { get; set; }
        public bool CompressVideos { get; set; }
        public int VideoResolution { get; set; }
        public int VideoCrf { get; set; }

        public FfmpegInfo Ffmpeg { get; set; } = new(false, false, null, null, null);
        public StorageUse Storage { get; set; } = new(0, 0, 0, 0, 0);
        public bool CanEdit { get; set; }

        public MediaSettings ToSettings() => new(MaxImageMb, MaxVideoMb, MaxOtherMb, OptimizeImages, ImageMaxWidth, ImageMaxHeight,
            ImageQuality, ConvertToWebP, CompressVideos, VideoResolution, VideoCrf);

        public static MediaSettingsViewModel From(MediaSettings s) => new()
        {
            MaxImageMb = s.MaxImageMb,
            MaxVideoMb = s.MaxVideoMb,
            MaxOtherMb = s.MaxOtherMb,
            OptimizeImages = s.OptimizeImages,
            ImageMaxWidth = s.ImageMaxWidth,
            ImageMaxHeight = s.ImageMaxHeight,
            ImageQuality = s.ImageQuality,
            ConvertToWebP = s.ConvertToWebP,
            CompressVideos = s.CompressVideos,
            VideoResolution = s.VideoResolution,
            VideoCrf = s.VideoCrf,
        };
    }

    /// <summary>The library's footprint: files, bytes stored, bytes optimization saved, videos waiting or failed.</summary>
    public sealed record StorageUse(int Files, long StoredBytes, long SavedBytes, int Queued, int Failed);

    [HttpGet("")]
    public async Task<IActionResult> Index() =>
        View(await BuildAsync(MediaSettingsViewModel.From(await _settings.GetAsync(HttpContext.RequestAborted))));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Save(MediaSettingsViewModel input)
    {
        // Without ffmpeg the Compress videos switch is disabled and posts nothing: keep the stored choice, so it is
        // still on once ffmpeg is set up.
        if (!(await _ffmpeg.GetInfoAsync()).Available)
        {
            input.CompressVideos = (await _settings.GetAsync(HttpContext.RequestAborted)).CompressVideos;
        }

        // SaveAsync writes the SettingsChanged audit entry.
        var errors = await _settings.SaveAsync(input.ToSettings(), HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(nameof(Index), await BuildAsync(input));
        }

        TempData["Success"] = "Media Library settings saved. They apply to new uploads.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<MediaSettingsViewModel> BuildAsync(MediaSettingsViewModel model)
    {
        var ct = HttpContext.RequestAborted;
        model.Ffmpeg = await _ffmpeg.GetInfoAsync();
        model.CanEdit = Can(PermissionAreas.Settings, PermissionActions.Update);
        model.Storage = new StorageUse(
            await _db.MediaFiles.CountAsync(ct),
            await _db.MediaFiles.SumAsync(m => (long?)m.SizeBytes, ct) ?? 0,
            await _db.MediaFiles.Where(m => m.OriginalSizeBytes != null).SumAsync(m => (long?)(m.OriginalSizeBytes!.Value - m.SizeBytes), ct) ?? 0,
            await _db.MediaFiles.CountAsync(m => m.Optimization == MediaOptimization.Pending || m.Optimization == MediaOptimization.Processing, ct),
            await _db.MediaFiles.CountAsync(m => m.Optimization == MediaOptimization.Failed, ct));
        return model;
    }
}

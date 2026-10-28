using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// The Media Library settings (.docs/features/media-storage.md#optimization): upload limits per kind of file and how
/// uploads are optimized to save storage.
/// </summary>
/// <param name="MaxImageMb">Largest image upload, in MB.</param>
/// <param name="MaxVideoMb">Largest video or audio upload, in MB.</param>
/// <param name="MaxOtherMb">Largest upload of any other file (PDF, documents, archives), in MB.</param>
/// <param name="OptimizeImages">Resize and re-encode JPEG, PNG and WebP uploads (<see cref="ImageOptimizer"/>).</param>
/// <param name="ImageMaxWidth">Wider images are scaled down to this width (px).</param>
/// <param name="ImageMaxHeight">Taller images are scaled down to this height (px).</param>
/// <param name="ImageQuality">JPEG/WebP quality, 40-100.</param>
/// <param name="ConvertToWebP">Store optimized JPEG and PNG images as WebP (usually much smaller).</param>
/// <param name="CompressVideos">Compress MP4 uploads in the background with ffmpeg (needs <c>FFMPEG_PATH</c>).</param>
/// <param name="VideoResolution">Larger videos are scaled down so their short side is at most this (480 = 480p … 2160 = 4K).</param>
/// <param name="VideoCrf">x264 constant rate factor: lower = better quality and larger files.</param>
public sealed record MediaSettings(
    int MaxImageMb, int MaxVideoMb, int MaxOtherMb,
    bool OptimizeImages, int ImageMaxWidth, int ImageMaxHeight, int ImageQuality, bool ConvertToWebP,
    bool CompressVideos, int VideoResolution, int VideoCrf)
{
    /// <summary>The highest upload limit that can be set, in MB.</summary>
    public const int MaxLimitMb = 2048;

    /// <summary>The choices for <see cref="VideoResolution"/>.</summary>
    public static readonly int[] VideoResolutions = [480, 720, 1080, 1440, 2160];

    /// <summary>The choices for <see cref="VideoCrf"/>: label and value.</summary>
    public static readonly (string Label, int Crf)[] VideoQualities = [("High quality", 20), ("Balanced", 23), ("Smallest files", 28)];

    public static MediaSettings Default { get; } = new(25, 100, 25, true, 2560, 2560, 82, false, false, 1080, 23);

    /// <summary>The upload limit for a file of this content type, in bytes.</summary>
    public long MaxBytesFor(string contentType) => KindOf(contentType) switch
    {
        MediaKind.Image => MaxImageMb * 1024L * 1024,
        MediaKind.Video => MaxVideoMb * 1024L * 1024,
        _ => MaxOtherMb * 1024L * 1024,
    };

    /// <summary>The largest of the three limits, in bytes (bounds the request size).</summary>
    public long LargestBytes => Math.Max(MaxImageMb, Math.Max(MaxVideoMb, MaxOtherMb)) * 1024L * 1024;

    public static MediaKind KindOf(string contentType) =>
        contentType.StartsWith("image/", StringComparison.Ordinal) ? MediaKind.Image
        : contentType.StartsWith("video/", StringComparison.Ordinal) || contentType.StartsWith("audio/", StringComparison.Ordinal) ? MediaKind.Video
        : MediaKind.Other;
}

/// <summary>The three groups the upload limits are set for.</summary>
public enum MediaKind
{
    Image,
    Video,
    Other,
}

/// <summary>
/// Reads and saves <see cref="MediaSettings"/> as rows of the <c>Settings</c> table (keys <c>media.*</c>); a missing or
/// unreadable row means the default.
/// </summary>
public sealed class MediaSettingsService
{
    public const string KeyPrefix = "media.";

    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;

    public MediaSettingsService(DotNetForgeDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<MediaSettings> GetAsync(CancellationToken ct = default)
    {
        var values = await _db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(KeyPrefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var d = MediaSettings.Default;
        int Int(string key, int fallback) => values.TryGetValue(KeyPrefix + key, out var v) && int.TryParse(v, out var n) ? n : fallback;
        bool Bool(string key, bool fallback) => values.TryGetValue(KeyPrefix + key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
        return new MediaSettings(
            Int("maxImageMb", d.MaxImageMb), Int("maxVideoMb", d.MaxVideoMb), Int("maxOtherMb", d.MaxOtherMb),
            Bool("optimizeImages", d.OptimizeImages), Int("imageMaxWidth", d.ImageMaxWidth), Int("imageMaxHeight", d.ImageMaxHeight),
            Int("imageQuality", d.ImageQuality), Bool("convertToWebP", d.ConvertToWebP),
            Bool("compressVideos", d.CompressVideos), Int("videoResolution", d.VideoResolution), Int("videoCrf", d.VideoCrf));
    }

    /// <summary>Validates and saves; returns the errors (nothing is saved when there are any).</summary>
    public async Task<List<string>> SaveAsync(MediaSettings input, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var (label, mb) in new[] { ("Images", input.MaxImageMb), ("Videos and audio", input.MaxVideoMb), ("Other files", input.MaxOtherMb) })
        {
            if (mb is < 1 or > MediaSettings.MaxLimitMb)
            {
                errors.Add($"{label}: the upload limit must be 1 to {MediaSettings.MaxLimitMb} MB.");
            }
        }

        if (input.ImageMaxWidth is < 320 or > 10000 || input.ImageMaxHeight is < 320 or > 10000)
        {
            errors.Add("The maximum image width and height must be 320 to 10000 px.");
        }

        if (input.ImageQuality is < 40 or > 100)
        {
            errors.Add("Image quality must be 40 to 100.");
        }

        if (!MediaSettings.VideoResolutions.Contains(input.VideoResolution))
        {
            errors.Add("Choose one of the offered video resolutions.");
        }

        if (!MediaSettings.VideoQualities.Any(q => q.Crf == input.VideoCrf))
        {
            errors.Add("Choose one of the offered video qualities.");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var values = new Dictionary<string, string>
        {
            ["maxImageMb"] = input.MaxImageMb.ToString(),
            ["maxVideoMb"] = input.MaxVideoMb.ToString(),
            ["maxOtherMb"] = input.MaxOtherMb.ToString(),
            ["optimizeImages"] = input.OptimizeImages ? "true" : "false",
            ["imageMaxWidth"] = input.ImageMaxWidth.ToString(),
            ["imageMaxHeight"] = input.ImageMaxHeight.ToString(),
            ["imageQuality"] = input.ImageQuality.ToString(),
            ["convertToWebP"] = input.ConvertToWebP ? "true" : "false",
            ["compressVideos"] = input.CompressVideos ? "true" : "false",
            ["videoResolution"] = input.VideoResolution.ToString(),
            ["videoCrf"] = input.VideoCrf.ToString(),
        };
        var rows = await _db.Settings.Where(s => s.Key.StartsWith(KeyPrefix)).ToListAsync(ct);
        foreach (var (name, value) in values)
        {
            var key = KeyPrefix + name;
            if (rows.FirstOrDefault(r => r.Key == key) is { } row)
            {
                row.Value = value;
            }
            else
            {
                _db.Settings.Add(new Setting { Key = key, Value = value });
            }
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", null, "Media Library settings",
            details: $"Limits {input.MaxImageMb}/{input.MaxVideoMb}/{input.MaxOtherMb} MB, images {(input.OptimizeImages ? "optimized" : "as uploaded")}, " +
                $"videos {(input.CompressVideos ? "compressed" : "as uploaded")}", cancellationToken: ct);
        return errors;
    }
}

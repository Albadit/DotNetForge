using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// Media uploads and deletions (.docs/features/media-storage.md). File bytes go to <see cref="IFileStorage"/> (never
/// the deployment directory); the <see cref="MediaFile"/> row holds the metadata and the opaque storage key. Upload
/// rules - allowed types, size cap, generated keys - live here so every caller enforces them.
/// </summary>
public sealed class MediaService
{
    /// <summary>
    /// Images up to this size are optimized (read into memory to re-encode); larger ones are stored as uploaded. The
    /// upload limits themselves are the Media Library settings (<see cref="MediaSettings"/>).
    /// </summary>
    public const long MaxOptimizedImageBytes = 64L * 1024 * 1024;

    /// <summary>Largest number of files in one upload request.</summary>
    public const int MaxFilesPerUpload = 10;

    /// <summary>
    /// Allowed extensions and the content type stored and served for each. The type is derived from the extension,
    /// never from the client's <c>Content-Type</c> header. Active formats (HTML, SVG, scripts) are deliberately
    /// excluded because they could run script when opened from the site's origin.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".avif"] = "image/avif",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".mp3"] = "audio/mpeg",
            [".mp4"] = "video/mp4",
            [".zip"] = "application/zip",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        };

    private readonly DotNetForgeDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IAuditService _audit;
    private readonly MediaSettingsService _settings;
    private readonly VideoOptimizationQueue _videos;
    private readonly Ffmpeg _ffmpeg;
    private readonly ILogger<MediaService> _logger;
    private MediaSettings? _current;

    public MediaService(DotNetForgeDbContext db, IFileStorage storage, IAuditService audit, MediaSettingsService settings,
        VideoOptimizationQueue videos, Ffmpeg ffmpeg, ILogger<MediaService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _settings = settings;
        _videos = videos;
        _ffmpeg = ffmpeg;
        _logger = logger;
    }

    /// <summary>The Media Library settings, read once per request.</summary>
    public async Task<MediaSettings> SettingsAsync(CancellationToken ct = default) => _current ??= await _settings.GetAsync(ct);

    /// <summary>
    /// Validates and stores one uploaded file. A file with the same name and size (as uploaded) already in
    /// <paramref name="folderId"/> is a duplicate: refused, or - with <paramref name="reuseDuplicate"/> (the pickers, which
    /// need a file back) and the same visibility - that existing file is returned instead of storing a second copy.
    /// Images are optimized on the way in and videos queued for compression, as the Media Library settings say
    /// (.docs/features/media-storage.md#optimization).
    /// </summary>
    /// <returns>The new (or reused) row, or a user-facing error message.</returns>
    public async Task<(MediaFile? File, string? Error)> UploadAsync(
        IFormFile upload, bool isPublic, Guid? userId, Guid? folderId = null, bool reuseDuplicate = false, CancellationToken ct = default)
    {
        var originalName = Path.GetFileName(upload.FileName ?? string.Empty);
        var storedOriginalName = Truncate(originalName, 400);
        var displayName = SanitizeFileName(originalName);
        if (upload.Length == 0)
        {
            return (null, $"'{displayName}' is empty.");
        }

        var extension = Path.GetExtension(originalName);
        if (!AllowedTypes.TryGetValue(extension, out var contentType))
        {
            return (null, $"'{displayName}' has a file type that is not allowed.");
        }

        var settings = await SettingsAsync(ct);
        var limit = settings.MaxBytesFor(contentType);
        if (upload.Length > limit)
        {
            var kind = MediaSettings.KindOf(contentType) switch
            {
                MediaKind.Image => "images",
                MediaKind.Video => "videos and audio",
                _ => "this kind of file",
            };
            return (null, $"'{displayName}' is larger than {limit / (1024 * 1024)} MB, the limit for {kind}.");
        }

        var duplicate = await _db.MediaFiles
            .Where(m => m.FolderId == folderId && m.OriginalName == storedOriginalName && (m.OriginalSizeBytes ?? m.SizeBytes) == upload.Length)
            .OrderByDescending(m => m.UploadedDate)
            .FirstOrDefaultAsync(ct);
        if (duplicate is not null)
        {
            if (reuseDuplicate && duplicate.IsPublic == isPublic)
            {
                return (duplicate, null);
            }

            if (!reuseDuplicate)
            {
                return (null, $"'{displayName}' is already in this folder (same name and size).");
            }
        }

        var optimized = settings.OptimizeImages && ImageOptimizer.Handles(extension) && upload.Length <= MaxOptimizedImageBytes
            ? await OptimizeImageAsync(upload, extension, settings, displayName, ct)
            : null;
        if (optimized is not null)
        {
            extension = optimized.Extension;
            contentType = optimized.ContentType;
            displayName = Truncate(Path.GetFileNameWithoutExtension(displayName) + extension, MaxFileNameLength);
        }

        var queueVideo = settings.CompressVideos && contentType == "video/mp4" && (await _ffmpeg.GetInfoAsync()).Available;

        // The key never contains user input: year / month / random id + normalized extension.
        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        try
        {
            await using var content = optimized is not null ? new MemoryStream(optimized.Data, writable: false) : upload.OpenReadStream();
            await _storage.SaveAsync(key, content, contentType, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage outages (network, credentials, quota) are an operator problem: log the cause, tell the user
            // plainly, and keep the rest of the batch going.
            _logger.LogError(ex, "Storing upload '{FileName}' as {StorageKey} failed.", displayName, key);
            await TryDeleteObjectAsync(key);
            return (null, $"'{displayName}' could not be stored right now. Try again later.");
        }

        var file = new MediaFile
        {
            FileName = displayName,
            OriginalName = storedOriginalName,
            ContentType = contentType,
            SizeBytes = optimized?.Data.LongLength ?? upload.Length,
            OriginalSizeBytes = optimized is not null ? upload.Length : null,
            Optimization = optimized is not null ? MediaOptimization.Optimized : queueVideo ? MediaOptimization.Pending : MediaOptimization.None,
            RelativePath = key,
            FolderId = folderId,
            IsPublic = isPublic,
            UploadedById = userId,
            UploadedDate = now,
        };

        try
        {
            _db.MediaFiles.Add(file);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Don't leave an orphaned object behind when the metadata can't be saved.
            await TryDeleteObjectAsync(key);
            throw;
        }

        await _audit.LogAsync(AuditActions.MediaUploaded, "MediaFile", file.Id.ToString(), file.FileName,
            details: optimized is not null ? $"Optimized from {upload.Length:N0} to {file.SizeBytes:N0} bytes" : null, cancellationToken: ct);
        if (queueVideo)
        {
            _videos.Notify();
        }

        return (file, null);
    }

    /// <summary>The smaller version of an uploaded image, or null (not smaller, not decodable, or the optimizer failed).</summary>
    private async Task<ImageOptimizer.Result?> OptimizeImageAsync(IFormFile upload, string extension, MediaSettings settings, string displayName, CancellationToken ct)
    {
        try
        {
            using var buffer = new MemoryStream((int)upload.Length);
            await using (var content = upload.OpenReadStream())
            {
                await content.CopyToAsync(buffer, ct);
            }

            return ImageOptimizer.Optimize(buffer.ToArray(), extension, settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never lose an upload over optimization: store it as it came.
            _logger.LogWarning(ex, "Optimizing image '{FileName}' failed; it is stored as uploaded.", displayName);
            return null;
        }
    }

    /// <summary>
    /// Makes a file public (anyone with the link) or private (signed-in admin users). Only the row's flag
    /// changes - the stored object stays where it is; <c>MediaFilesController</c> checks the flag on every download.
    /// </summary>
    public async Task SetVisibilityAsync(MediaFile file, bool isPublic, CancellationToken ct = default)
    {
        if (file.IsPublic == isPublic)
        {
            return;
        }

        file.IsPublic = isPublic;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaUpdated, "MediaFile", file.Id.ToString(), file.FileName,
            details: isPublic ? "Made public" : "Made private", cancellationToken: ct);
    }

    /// <summary>Longest file name (with the extension).</summary>
    public const int MaxFileNameLength = 200;

    /// <summary>
    /// Renames a file: only its display and download name changes. The extension stays - the stored and served type
    /// depends on it - so a typed extension is ignored and the file's own is kept. The stored object and the id stay,
    /// and <c>MediaFilesController</c> finds a file by its id alone, so links with the old name keep working.
    /// </summary>
    /// <returns>A user-facing error, or null (renamed, or the name is unchanged).</returns>
    public async Task<string?> RenameAsync(MediaFile file, string? name, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(file.FileName);
        var stem = (name ?? string.Empty).Trim();
        if (extension.Length > 0 && stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^extension.Length];
        }

        stem = new string(stem.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/')).ToArray()).Trim().TrimEnd('.');
        if (stem.Length == 0)
        {
            return "Enter a name.";
        }

        if (stem.Length + extension.Length > MaxFileNameLength)
        {
            return $"The name can have at most {MaxFileNameLength - extension.Length} characters.";
        }

        var renamed = stem + extension;
        if (renamed == file.FileName)
        {
            return null;
        }

        var before = file.FileName;
        file.FileName = renamed;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.MediaUpdated, "MediaFile", file.Id.ToString(), file.FileName,
            details: $"Renamed from '{before}'", cancellationToken: ct);
        return null;
    }

    /// <summary>Deletes the row first (so nothing references a missing object), then the stored object.</summary>
    public async Task DeleteAsync(MediaFile file, CancellationToken ct = default)
    {
        _db.MediaFiles.Remove(file);
        await _db.SaveChangesAsync(ct);
        await TryDeleteObjectAsync(file.RelativePath);
        await _audit.LogAsync(AuditActions.MediaDeleted, "MediaFile", file.Id.ToString(), file.FileName,
            cancellationToken: ct);
    }

    /// <summary>Types the browser may display inline; everything else downloads as an attachment.</summary>
    public static bool IsInline(string contentType) =>
        contentType.StartsWith("image/", StringComparison.Ordinal) ||
        contentType.StartsWith("video/", StringComparison.Ordinal) ||
        contentType.StartsWith("audio/", StringComparison.Ordinal) ||
        contentType == "application/pdf";

    /// <summary>The public URL path of a media file (authorization is applied when it is requested).</summary>
    public static string UrlFor(MediaFile file) => UrlFor(file.Id, file.FileName);

    public static string UrlFor(Guid id, string fileName) => DotNetForge.Shared.Media.MediaUrls.For(id, fileName);

    private async Task TryDeleteObjectAsync(string key)
    {
        try
        {
            await _storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The row is gone (or was never saved); an unreachable object is only wasted space, so log and continue.
            _logger.LogWarning(ex, "Could not delete stored object {StorageKey}; it is now orphaned.", key);
        }
    }

    /// <summary>A display/download name: no path, no control characters, bounded length.</summary>
    private static string SanitizeFileName(string name)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/')).ToArray()).Trim();
        return Truncate(cleaned.Length == 0 ? "file" : cleaned, MaxFileNameLength);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;

namespace DotNetForge.Web.Services;

/// <summary>
/// Media uploads and deletions (.docs/features/media-storage.md). File bytes go to <see cref="IFileStorage"/> (never
/// the deployment directory); the <see cref="MediaFile"/> row holds the metadata and the opaque storage key. Upload
/// rules - allowed types, size cap, generated keys - live here so every caller enforces them.
/// </summary>
public sealed class MediaService
{
    /// <summary>Largest accepted file. Also bounds the upload request size (see the admin MediaController).</summary>
    public const long MaxUploadBytes = 25L * 1024 * 1024;

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
    private readonly ILogger<MediaService> _logger;

    public MediaService(DotNetForgeDbContext db, IFileStorage storage, IAuditService audit, ILogger<MediaService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>Validates and stores one uploaded file for the tenant.</summary>
    /// <returns>The new row, or a user-facing error message.</returns>
    public async Task<(MediaFile? File, string? Error)> UploadAsync(
        IFormFile upload, bool isPublic, Guid tenantId, Guid? userId, CancellationToken ct = default)
    {
        var originalName = Path.GetFileName(upload.FileName ?? string.Empty);
        var displayName = SanitizeFileName(originalName);
        if (upload.Length == 0)
        {
            return (null, $"'{displayName}' is empty.");
        }

        if (upload.Length > MaxUploadBytes)
        {
            return (null, $"'{displayName}' is larger than {MaxUploadBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(originalName);
        if (!AllowedTypes.TryGetValue(extension, out var contentType))
        {
            return (null, $"'{displayName}' has a file type that is not allowed.");
        }

        // The key never contains user input: tenant / year / month / random id + normalized extension.
        var now = DateTime.UtcNow;
        var key = $"{tenantId:N}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        try
        {
            await using var content = upload.OpenReadStream();
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
            TenantId = tenantId,
            FileName = displayName,
            OriginalName = Truncate(originalName, 400),
            ContentType = contentType,
            SizeBytes = upload.Length,
            RelativePath = key,
            FolderPath = "/",
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
            cancellationToken: ct);
        return (file, null);
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

    public static string UrlFor(Guid id, string fileName) => $"/media/{id}/{Uri.EscapeDataString(fileName)}";

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
        return Truncate(cleaned.Length == 0 ? "file" : cleaned, 200);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

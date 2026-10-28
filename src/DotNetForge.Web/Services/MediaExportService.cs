using System.IO.Compression;
using System.Text.Json;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>
/// The media export of Data Management (.docs/pages/data-management.md#media-export): every media file in a .zip, in
/// its media-library folders (<c>Photos/2026/beach.jpg</c>), plus <c>media.json</c> with each file's id, link,
/// visibility and sizes. Streamed straight into the response - nothing is buffered in memory or on disk, however large
/// the library. Download only (it is not imported). Every run gets a <see cref="TransferRecord"/> and an audit entry.
/// "Database and media" puts a database export next to the files: the export file at the root, the files under
/// <c>media/</c>.
/// </summary>
public sealed class MediaExportService
{
    public const string ManifestName = "media.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly DotNetForgeDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IAuditService _audit;
    private readonly ILogger<MediaExportService> _logger;

    public MediaExportService(DotNetForgeDbContext db, IFileStorage storage, IAuditService audit, ILogger<MediaExportService> logger)
    {
        _db = db;
        _storage = storage;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>What the export will contain, for the Data Management screen.</summary>
    public async Task<(int Files, long Bytes)> SummaryAsync(CancellationToken ct) =>
        (await _db.MediaFiles.CountAsync(ct), await _db.MediaFiles.SumAsync(m => (long?)m.SizeBytes, ct) ?? 0);

    /// <summary>The folder of the files when a database export is included.</summary>
    public const string MediaFolder = "media";

    /// <summary>"dotnetforge-media-20261028-101500.zip", or "dotnetforge-full-…" with the database export.</summary>
    public static string FileName(DateTime utc, bool withDatabase = false) =>
        $"dotnetforge-{(withDatabase ? "full" : "media")}-{utc:yyyyMMdd-HHmmss}.zip";

    /// <summary>
    /// Writes the export to <paramref name="output"/> (the response body). A stored object that is missing is listed in
    /// <c>media.json</c> with <c>"missing": true</c> instead of failing the export. With <paramref name="database"/> (an
    /// export of <see cref="DatabaseTransferService.ExportAsync"/>, recorded there) that file goes in unchanged, so it
    /// can be imported as it is.
    /// </summary>
    public async Task WriteAsync(Stream output, string fileName, Guid? userId, CancellationToken ct,
        DatabaseTransferService.ExportFile? database = null)
    {
        var root = database is null ? string.Empty : MediaFolder + "/";
        var record = new TransferRecord
        {
            Operation = TransferOperation.MediaExport,
            Status = TransferStatus.Failed,
            Provider = "Media storage",
            FileName = fileName,
            InitiatedById = userId,
            StartedAt = DateTime.UtcNow,
        };

        var folders = await _db.MediaFolders.AsNoTracking().OrderBy(f => f.Path).Select(f => new { f.Id, f.Path }).ToListAsync(ct);
        var pathOf = folders.ToDictionary(f => f.Id, f => root + f.Path.TrimStart('/'));
        var files = await _db.MediaFiles.AsNoTracking().OrderBy(m => m.FolderId).ThenBy(m => m.FileName).ToListAsync(ct);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root + ManifestName };
        var entries = new List<object>();
        var missing = 0;
        var body = new AsyncWriteStream(output);
        try
        {
            await using (var zip = await ZipArchive.CreateAsync(body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct))
            {
                if (database is not null)
                {
                    // A .zip already: stored as it is.
                    var export = zip.CreateEntry(database.FileName, CompressionLevel.NoCompression);
                    await using (var target = await export.OpenAsync(ct))
                    {
                        await database.Content.CopyToAsync(target, ct);
                    }

                    zip.CreateEntry(root);
                }

                // Every folder, also empty ones, so the tree is complete.
                foreach (var folder in folders)
                {
                    zip.CreateEntry(pathOf[folder.Id] + "/");
                }

                foreach (var file in files)
                {
                    var folder = file.FolderId is { } id && pathOf.TryGetValue(id, out var p) ? p : null;
                    var path = Unique(folder is null ? root + file.FileName : folder + "/" + file.FileName, used);
                    await using var stored = await _storage.OpenReadAsync(file.RelativePath, ct);
                    if (stored is null)
                    {
                        missing++;
                    }
                    else
                    {
                        var entry = zip.CreateEntry(path, Compresses(file.ContentType) ? CompressionLevel.Fastest : CompressionLevel.NoCompression);
                        entry.LastWriteTime = DateTime.SpecifyKind(file.UploadedDate, DateTimeKind.Utc);
                        await using var target = await entry.OpenAsync(ct);
                        await stored.Content.CopyToAsync(target, ct);
                        record.SizeBytes += file.SizeBytes;
                        record.RecordCount++;
                    }

                    entries.Add(new
                    {
                        file.Id,
                        Path = stored is null ? null : path,
                        file.FileName,
                        file.OriginalName,
                        file.ContentType,
                        file.SizeBytes,
                        file.OriginalSizeBytes,
                        file.IsPublic,
                        Folder = folder is null ? "/" : "/" + folder[root.Length..],
                        file.UploadedDate,
                        Url = MediaService.UrlFor(file),
                        Missing = stored is null,
                    });
                }

                var manifest = zip.CreateEntry(root + ManifestName, CompressionLevel.Optimal);
                await using (var json = await manifest.OpenAsync(ct))
                {
                    await JsonSerializer.SerializeAsync(json, new
                    {
                        Format = "dotnetforge-media",
                        Version = 1,
                        ExportedAt = record.StartedAt,
                        Database = database?.FileName,
                        Folders = folders.Select(f => f.Path),
                        Files = entries,
                    }, Json, ct);
                }
            }

            await body.FlushAsync(ct);
            record.Status = TransferStatus.Success;
            record.Note = string.Join("; ", new[]
            {
                database is null ? null : "With " + database.FileName,
                missing > 0 ? $"{missing} stored file(s) missing" : null,
            }.OfType<string>()) is { Length: > 0 } note ? note : null;
        }
        catch (OperationCanceledException)
        {
            record.Error = "Cancelled: the download was stopped.";
            throw;
        }
        catch (Exception ex)
        {
            // The download is cut off; the browser shows it as failed.
            _logger.LogError(ex, "Media export {FileName} failed.", fileName);
            record.Error = $"Failed ({ex.GetType().Name}); see the log.";
            throw;
        }
        finally
        {
            record.CompletedAt = DateTime.UtcNow;
            _db.TransferRecords.Add(record);
            await _db.SaveChangesAsync(CancellationToken.None);
            await _audit.LogAsync(AuditActions.TransferExported, nameof(TransferRecord), record.Id.ToString(), record.FileName,
                record.Status == TransferStatus.Success
                    ? $"Media: {record.RecordCount} files" + (missing > 0 ? $", {missing} missing" : null) + (database is null ? null : $", with {database.FileName}")
                    : record.Error,
                success: record.Status == TransferStatus.Success, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// The response body for <see cref="ZipArchive"/>: even with its async API it writes some headers synchronously (empty
    /// entries such as folders, the central directory), which the response body refuses. Those few bytes are kept here and
    /// sent with the next asynchronous write or flush; file contents go straight through.
    /// </summary>
    private sealed class AsyncWriteStream(Stream inner) : Stream
    {
        private readonly MemoryStream _pending = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await SendPendingAsync(cancellationToken);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
            // Sent with the next asynchronous write or flush.
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await SendPendingAsync(cancellationToken);
            await inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        private async Task SendPendingAsync(CancellationToken ct)
        {
            if (_pending.Length > 0)
            {
                await inner.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), ct);
                _pending.SetLength(0);
            }
        }
    }

    /// <summary>Text compresses; images, video, audio, PDFs and archives are compressed already.</summary>
    private static bool Compresses(string contentType) =>
        contentType.StartsWith("text/", StringComparison.Ordinal);

    /// <summary>The path, or "name (2).ext" when the folder already has a file of that name.</summary>
    private static string Unique(string path, HashSet<string> used)
    {
        var candidate = path;
        var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = (string.IsNullOrEmpty(dir) ? string.Empty : dir + "/") + $"{stem} ({n}){ext}";
        }

        return candidate;
    }
}

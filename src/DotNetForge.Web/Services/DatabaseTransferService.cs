using System.Security.Cryptography;
using DotNetForge.Abstractions.Database;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Core.Transfer;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Shared.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DotNetForge.Web.Services;

/// <summary>
/// The Data Management screen's operations (.docs/pages/data-management.md): export (download), backups kept in the file storage
/// (manual or on the schedule), and - in Development only - import of an export file and restore of a backup, which
/// replace all CMS data. Every run gets a <see cref="TransferRecord"/> and an audit entry; one run at a time per
/// process. The file format is <see cref="TransferFile"/>.
/// </summary>
public sealed class DatabaseTransferService
{
    public const string KeyPrefix = "transfer.";
    private const string BackupEnabledKey = "transfer.backup.enabled";
    private const string BackupIntervalKey = "transfer.backup.interval-hours";
    private const string BackupKeepKey = "transfer.backup.keep";
    private const string ZipContentType = "application/zip";

    /// <summary>The schedule's choices, in hours (the select on the screen).</summary>
    public static readonly int[] Intervals = [6, 12, 24, 168];

    // One export, backup, import or restore at a time: an import must never run next to another transfer.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly DotNetForgeDbContext _db;
    private readonly DatabaseCatalog _catalog;
    private readonly IDatabaseService _databases;
    private readonly IFileStorage _storage;
    private readonly IAuditService _audit;
    private readonly IMemoryCache _cache;
    private readonly IRoleGrantStore _grants;
    private readonly IHostEnvironment _env;
    private readonly ILogger<DatabaseTransferService> _logger;

    public DatabaseTransferService(DotNetForgeDbContext db, DatabaseCatalog catalog, IDatabaseService databases, IFileStorage storage,
        IAuditService audit, IMemoryCache cache, IHostEnvironment env, ILogger<DatabaseTransferService> logger, IRoleGrantStore grants)
    {
        _db = db;
        _catalog = catalog;
        _databases = databases;
        _storage = storage;
        _audit = audit;
        _cache = cache;
        _grants = grants;
        _env = env;
        _logger = logger;
    }

    /// <summary>The CMS database's provider, <c>PostgreSQL</c>.</summary>
    public string Provider => _catalog.Main.Provider.DisplayName;

    /// <summary>What Export offers: the CMS database (null, the default), then every additional database.</summary>
    public IReadOnlyList<(string? Name, string Label)> ExportDatabases =>
        _catalog.All
            .Select(d => d.Settings.IsMain
                ? ((string?)null, $"CMS database ({d.Provider.DisplayName})")
                : (d.Name, $"{d.Name} ({d.Provider.DisplayName})"))
            .ToList();

    /// <summary>Import and restore replace all data: Development only (production: export and backups only).</summary>
    public bool ImportAllowed => _env.IsDevelopment();

    /// <summary>An export in a temporary file (deleted when disposed).</summary>
    public sealed record ExportFile(Stream Content, string FileName, int RecordCount) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Content.DisposeAsync();
    }

    /// <summary>The outcome of a run; <see cref="Error"/> is shown to the Super Admin.</summary>
    public sealed record TransferOutcome(bool Succeeded, string? Error, TransferRecord? Record)
    {
        public static TransferOutcome Busy { get; } =
            new(false, "Another export, backup or import is running. Try again when it has finished.", null);
    }

    public sealed record BackupSchedule(bool Enabled, int IntervalHours, int Keep)
    {
        public static BackupSchedule Default { get; } = new(false, 24, 7);
    }

    /// <summary>
    /// The export for download - of the CMS database (<paramref name="database"/> null: importable) or of an additional
    /// database (every table/collection as JSON; not importable) - or the problem (unknown database, another run busy,
    /// or the export failed).
    /// </summary>
    public async Task<(ExportFile? File, string? Error)> ExportAsync(Guid? userId, string? database, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(database)
            ? null
            : _catalog.All.FirstOrDefault(d => !d.Settings.IsMain && d.Name == database.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(database) && target is null)
        {
            return (null, $"There is no database '{database}'.");
        }

        if (!await Gate.WaitAsync(0, ct))
        {
            return (null, TransferOutcome.Busy.Error);
        }

        try
        {
            var record = Start(TransferOperation.Export, userId);
            try
            {
                var file = target is null
                    ? await WriteExportAsync(record, "export", ct)
                    : await WriteDatabaseExportAsync(record, target, ct);
                await FinishAsync(record, AuditActions.TransferExported, record.Note, ct);
                return (file, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FailAsync(record, AuditActions.TransferExported, ex, ct);
                return (null, $"The export failed ({ex.GetType().Name}); see the log.");
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Stores a backup in the file storage (<c>backups/…</c>).</summary>
    public async Task<TransferOutcome> CreateBackupAsync(Guid? userId, bool automatic, CancellationToken ct)
    {
        if (!await Gate.WaitAsync(0, ct))
        {
            return TransferOutcome.Busy;
        }

        try
        {
            var outcome = await BackupAsync(userId, automatic, note: null, ct);
            if (outcome.Succeeded && automatic)
            {
                await PruneAsync(ct);
            }

            return outcome;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>The stored backups, newest first.</summary>
    public Task<List<TransferRecord>> ListBackupsAsync(CancellationToken ct) =>
        _db.TransferRecords.AsNoTracking()
            .Where(r => r.Operation == TransferOperation.Backup && r.Status == TransferStatus.Success && r.StorageKey != null)
            .OrderByDescending(r => r.StartedAt)
            .ToListAsync(ct);

    /// <summary>The latest runs of every kind, newest first.</summary>
    public Task<List<TransferRecord>> HistoryAsync(int count, CancellationToken ct) =>
        _db.TransferRecords.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(count).ToListAsync(ct);

    /// <summary>A stored backup for download: a signed URL when the storage can issue one, else the stream; null when gone.</summary>
    public async Task<(TransferRecord Backup, Uri? Url, StoredFile? File)?> OpenBackupAsync(Guid id, string contentDisposition, CancellationToken ct)
    {
        if (await FindBackupAsync(id, ct) is not { } backup)
        {
            return null;
        }

        var url = await _storage.GetDownloadUrlAsync(backup.StorageKey!, contentDisposition, TimeSpan.FromMinutes(5), ct);
        var file = url is null ? await _storage.OpenReadAsync(backup.StorageKey!, ct) : null;
        if (url is null && file is null)
        {
            _logger.LogWarning("Backup {BackupId} has no stored object at {StorageKey}.", id, backup.StorageKey);
            return null;
        }

        await _audit.LogAsync(AuditActions.BackupDownloaded, nameof(TransferRecord), id.ToString(), backup.FileName, cancellationToken: ct);
        return (backup, url, file);
    }

    /// <summary>Deletes a stored backup; its history entry stays (without the file).</summary>
    public async Task<bool> DeleteBackupAsync(Guid id, CancellationToken ct)
    {
        var backup = await _db.TransferRecords.FirstOrDefaultAsync(r => r.Id == id && r.StorageKey != null, ct);
        if (backup is null)
        {
            return false;
        }

        await _storage.DeleteAsync(backup.StorageKey!, ct);
        backup.StorageKey = null;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.BackupDeleted, nameof(TransferRecord), id.ToString(), backup.FileName, cancellationToken: ct);
        return true;
    }

    /// <summary>Development only: replaces all data with an uploaded export, after a safety backup of the current data.</summary>
    public Task<TransferOutcome> ImportAsync(Stream upload, string fileName, Guid? userId, CancellationToken ct) =>
        ReplaceAsync(TransferOperation.Import, upload, fileName, userId, ct);

    /// <summary>Development only: replaces all data with a stored backup, after a safety backup of the current data.</summary>
    public async Task<TransferOutcome> RestoreAsync(Guid id, Guid? userId, CancellationToken ct)
    {
        if (await FindBackupAsync(id, ct) is not { } backup)
        {
            return new TransferOutcome(false, "That backup no longer exists.", null);
        }

        await using var stored = await _storage.OpenReadAsync(backup.StorageKey!, ct);
        if (stored is null)
        {
            return new TransferOutcome(false, "The backup's file is missing from the storage.", null);
        }

        return await ReplaceAsync(TransferOperation.Restore, stored.Content, backup.FileName, userId, ct);
    }

    public async Task<BackupSchedule> GetScheduleAsync(CancellationToken ct)
    {
        var values = await _db.Settings.AsNoTracking().Where(s => s.Key.StartsWith(KeyPrefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var fallback = BackupSchedule.Default;
        return new BackupSchedule(
            values.GetValueOrDefault(BackupEnabledKey) == "true",
            int.TryParse(values.GetValueOrDefault(BackupIntervalKey), out var interval) && Intervals.Contains(interval) ? interval : fallback.IntervalHours,
            int.TryParse(values.GetValueOrDefault(BackupKeepKey), out var keep) && keep is >= 1 and <= 100 ? keep : fallback.Keep);
    }

    /// <summary>Saves the automatic backup schedule; returns the problem, or null.</summary>
    public async Task<string?> SaveScheduleAsync(BackupSchedule schedule, CancellationToken ct)
    {
        if (!Intervals.Contains(schedule.IntervalHours))
        {
            return "Choose how often to back up.";
        }

        if (schedule.Keep is < 1 or > 100)
        {
            return "Keep between 1 and 100 automatic backups.";
        }

        var rows = await _db.Settings.Where(s => s.Key.StartsWith(KeyPrefix)).ToListAsync(ct);
        void Set(string key, string value)
        {
            if (rows.FirstOrDefault(r => r.Key == key) is { } row)
            {
                row.Value = value;
            }
            else
            {
                _db.Settings.Add(new Setting { Key = key, Value = value });
            }
        }

        Set(BackupEnabledKey, schedule.Enabled ? "true" : "false");
        Set(BackupIntervalKey, schedule.IntervalHours.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(BackupKeepKey, schedule.Keep.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", "transfer.backup", "Backup schedule",
            schedule.Enabled ? $"Every {schedule.IntervalHours} h, keep {schedule.Keep}" : "Off", cancellationToken: ct);
        return null;
    }

    /// <summary>For the scheduler: whether an automatic backup is due now.</summary>
    public async Task<bool> BackupDueAsync(CancellationToken ct)
    {
        var schedule = await GetScheduleAsync(ct);
        if (!schedule.Enabled || !await _db.SystemState.AnyAsync(s => s.Installed, ct))
        {
            return false;
        }

        var last = await _db.TransferRecords.AsNoTracking()
            .Where(r => r.Operation == TransferOperation.Backup && r.Status == TransferStatus.Success)
            .OrderByDescending(r => r.StartedAt).Select(r => (DateTime?)r.StartedAt).FirstOrDefaultAsync(ct);
        return last is null || DateTime.SpecifyKind(last.Value, DateTimeKind.Utc).AddHours(schedule.IntervalHours) <= DateTime.UtcNow;
    }

    private async Task<TransferOutcome> ReplaceAsync(TransferOperation operation, Stream source, string fileName, Guid? userId, CancellationToken ct)
    {
        if (!ImportAllowed)
        {
            // The controller hides and refuses it too; this guards every other caller.
            throw new InvalidOperationException("Import and restore are only available in Development.");
        }

        if (!await Gate.WaitAsync(0, ct))
        {
            return TransferOutcome.Busy;
        }

        var action = operation == TransferOperation.Import ? AuditActions.TransferImported : AuditActions.BackupRestored;
        var record = Start(operation, userId);
        record.FileName = Truncate(Path.GetFileName(fileName), 260) ?? "export.zip";
        try
        {
            await using var file = TempFile();
            await source.CopyToAsync(file, ct);
            record.SizeBytes = file.Length;
            file.Position = 0;
            record.Checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            file.Position = 0;

            // Everything is checked before anything changes.
            var content = await TransferFile.ReadAsync(_db, file, ct);
            record.SourceProvider = Truncate(content.Manifest?.Provider, 50);
            if (content.Errors.Count > 0)
            {
                return await RefuseAsync(record, action, string.Join(" ", content.Errors.Take(5)));
            }

            // No backup, no import: the current data is kept in the storage first.
            var safety = await BackupAsync(userId, automatic: false,
                note: Truncate($"Before {(operation == TransferOperation.Import ? "import" : "restore")} of {record.FileName}", 200), ct);
            if (!safety.Succeeded)
            {
                return await RefuseAsync(record, action, "The backup of the current data failed, so nothing was imported: " + safety.Error);
            }

            await TransferFile.ReplaceAsync(_db, content, ct);
            record.RecordCount = content.Manifest!.RecordCount;
            ClearCaches();
            await FinishAsync(record, action, content.Manifest.Provider + " › " + Provider, ct);
            return new TransferOutcome(true, null, record);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "{Operation} of {FileName} failed; the data is unchanged.", operation, record.FileName);
            _db.ChangeTracker.Clear();
            return await RefuseAsync(record, action, $"The import failed and was rolled back - the data is unchanged ({ex.GetType().Name}).");
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>A backup into the storage; the caller holds the gate.</summary>
    private async Task<TransferOutcome> BackupAsync(Guid? userId, bool automatic, string? note, CancellationToken ct)
    {
        var record = Start(TransferOperation.Backup, userId);
        record.Automatic = automatic;
        record.Note = note;
        try
        {
            await using var file = await WriteExportAsync(record, "backup", ct);
            var key = $"backups/{record.Id:N}.zip";
            await _storage.SaveAsync(key, file.Content, ZipContentType, ct);
            record.StorageKey = key;
            await FinishAsync(record, AuditActions.BackupCreated, note ?? (automatic ? "Automatic" : null), ct);
            return new TransferOutcome(true, null, record);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FailAsync(record, AuditActions.BackupCreated, ex, ct);
            return new TransferOutcome(false, $"The backup failed ({ex.GetType().Name}); see the log.", record);
        }
    }

    /// <summary>Deletes automatic backups beyond the schedule's "keep" count (manual and safety backups stay).</summary>
    private async Task PruneAsync(CancellationToken ct)
    {
        var keep = (await GetScheduleAsync(ct)).Keep;
        var old = await _db.TransferRecords
            .Where(r => r.Automatic && r.StorageKey != null)
            .OrderByDescending(r => r.StartedAt).Skip(keep).ToListAsync(ct);
        foreach (var backup in old)
        {
            await _storage.DeleteAsync(backup.StorageKey!, ct);
            backup.StorageKey = null;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActions.BackupDeleted, nameof(TransferRecord), backup.Id.ToString(), backup.FileName,
                "Older than the newest " + keep + " automatic backups", cancellationToken: ct);
        }
    }

    private async Task<ExportFile> WriteDatabaseExportAsync(TransferRecord record, ResolvedDatabase target, CancellationToken ct)
    {
        var file = TempFile();
        try
        {
            var provider = target.Provider.DisplayName;
            record.Provider = provider;
            record.Note = $"Database {target.Name}";
            record.RecordCount = await TransferFile.WriteDatabaseAsync(_databases, target.Name, provider, file, ct);
            record.FileName = TransferRules.FileName(provider, DateTime.UtcNow, "export-" + target.Name);
            record.SizeBytes = file.Length;
            file.Position = 0;
            record.Checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            file.Position = 0;
            return new ExportFile(file, record.FileName, record.RecordCount);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private async Task<ExportFile> WriteExportAsync(TransferRecord record, string kind, CancellationToken ct)
    {
        var file = TempFile();
        try
        {
            var manifest = await TransferFile.WriteAsync(_db, file, Provider, ct);
            record.FileName = TransferRules.FileName(Provider, manifest.CreatedAt, kind);
            record.RecordCount = manifest.RecordCount;
            record.SizeBytes = file.Length;
            file.Position = 0;
            record.Checksum = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            file.Position = 0;
            return new ExportFile(file, record.FileName, manifest.RecordCount);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private TransferRecord Start(TransferOperation operation, Guid? userId) => new()
    {
        Operation = operation,
        Status = TransferStatus.Failed,
        Provider = Provider,
        InitiatedById = userId,
        StartedAt = DateTime.UtcNow,
    };

    private async Task FinishAsync(TransferRecord record, string action, string? details, CancellationToken ct)
    {
        record.Status = TransferStatus.Success;
        record.CompletedAt = DateTime.UtcNow;
        _db.TransferRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(action, nameof(TransferRecord), record.Id.ToString(), record.FileName, details, cancellationToken: ct);
    }

    private async Task FailAsync(TransferRecord record, string action, Exception ex, CancellationToken ct)
    {
        _logger.LogError(ex, "{Operation} failed.", record.Operation);
        _db.ChangeTracker.Clear();
        await RefuseAsync(record, action, $"Failed ({ex.GetType().Name}); see the log.");
    }

    private async Task<TransferOutcome> RefuseAsync(TransferRecord record, string action, string error)
    {
        record.Status = TransferStatus.Failed;
        record.CompletedAt = DateTime.UtcNow;
        record.Error = Truncate(error, 2000);
        if (string.IsNullOrEmpty(record.FileName))
        {
            record.FileName = "-";
        }

        _db.TransferRecords.Add(record);
        await _db.SaveChangesAsync(CancellationToken.None);
        await _audit.LogAsync(action, nameof(TransferRecord), record.Id.ToString(), record.FileName, record.Error,
            success: false, cancellationToken: CancellationToken.None);
        return new TransferOutcome(false, error, record);
    }

    private Task<TransferRecord?> FindBackupAsync(Guid id, CancellationToken ct) =>
        _db.TransferRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.StorageKey != null, ct);

    /// <summary>Settings, tokens and the like are cached in memory; after an import they describe the old data.</summary>
    private void ClearCaches()
    {
        if (_cache is MemoryCache memory)
        {
            memory.Clear();
        }

        _grants.Invalidate();
    }

    /// <summary>A temporary file for an export (outside the read-only deployment directory; deleted when closed).</summary>
    private static FileStream TempFile() =>
        new(Path.Combine(Path.GetTempPath(), $"dotnetforge-transfer-{Guid.NewGuid():N}.zip"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}

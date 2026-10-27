using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Entities;

/// <summary>
/// One export, backup, import or restore on Data Management (.docs/pages/data-management.md) - its result log. A backup
/// kept in the file storage has a <see cref="StorageKey"/>; it is cleared when the backup is deleted, so the history
/// stays. Not part of exports: an import never replaces this instance's own transfer history.
/// </summary>
public class TransferRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TransferOperation Operation { get; set; }

    public TransferStatus Status { get; set; }

    /// <summary>This instance's database provider (the source of an export or backup, the target of an import).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The provider the imported file was exported from; null for exports and backups.</summary>
    public string? SourceProvider { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Where a backup is kept in the file storage (<c>backups/…</c>); null when not stored or deleted.</summary>
    public string? StorageKey { get; set; }

    public long SizeBytes { get; set; }

    public int RecordCount { get; set; }

    /// <summary>SHA-256 (hex) of the export file.</summary>
    public string? Checksum { get; set; }

    /// <summary>Why it failed (no secrets, no stack traces).</summary>
    public string? Error { get; set; }

    /// <summary>A scheduled backup: only these are pruned to the "keep" count of the schedule.</summary>
    public bool Automatic { get; set; }

    /// <summary>E.g. which import a safety backup was made before.</summary>
    public string? Note { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    /// <summary>The Super Admin who started it; null for automatic backups.</summary>
    public Guid? InitiatedById { get; set; }
}

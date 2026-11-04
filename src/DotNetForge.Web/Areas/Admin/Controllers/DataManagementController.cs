using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using DotNetForge.Web.Services.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Settings → Administration Panel → Data Management (.docs/pages/data-management.md), Super Admin only. Export (download) and backups
/// (stored, manual or scheduled) work everywhere; import and restore replace all data and exist only in Development.
/// Rules, records and audit: <see cref="DatabaseTransferService"/>.
/// </summary>
[Route("admin/data")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class DataManagementController : AdminControllerBase
{
    /// <summary>The largest export the import accepts.</summary>
    public const long MaxImportBytes = 512L * 1024 * 1024;

    private readonly DatabaseTransferService _transfer;
    private readonly MediaExportService _mediaExport;
    private readonly DotNetForgeDbContext _db;

    public DataManagementController(DatabaseTransferService transfer, MediaExportService mediaExport, DotNetForgeDbContext db)
    {
        _transfer = transfer;
        _mediaExport = mediaExport;
        _db = db;
    }

    public sealed class DataManagementViewModel
    {
        public required string Provider { get; init; }

        /// <summary>The Export select: the CMS database (null) first, then the additional databases.</summary>
        public required IReadOnlyList<(string? Name, string Label)> ExportDatabases { get; init; }
        public required bool ImportAllowed { get; init; }
        public required DatabaseTransferService.BackupSchedule Schedule { get; init; }
        public required List<TransferRecord> Backups { get; init; }
        public required List<TransferRecord> History { get; init; }

        /// <summary>What the media export holds: files and their stored size.</summary>
        public required (int Files, long Bytes) Media { get; init; }

        /// <summary>Who started a run (email), by user id.</summary>
        public required Dictionary<Guid, string> Users { get; init; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await BuildAsync());

    [HttpPost("export")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(string? database)
    {
        // ExportAsync writes the TransferExported audit entry.
        var (file, error) = await _transfer.ExportAsync(CurrentUserId, database, HttpContext.RequestAborted);
        if (file is null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        // FileStreamResult disposes the temporary file (deleted on close) after the response.
        return File(file.Content, "application/zip", file.FileName);
    }

    /// <summary>
    /// Every media file in its library folders plus <c>media.json</c>, streamed as it is read from the storage (no
    /// temporary copy, however large the library). <see cref="MediaExportService"/> records it and writes the audit entry.
    /// </summary>
    [HttpPost("export-media")]
    [ValidateAntiForgeryToken]
    public async Task ExportMedia()
    {
        var fileName = MediaExportService.FileName(DateTime.UtcNow);
        StartZipDownload(fileName);
        await _mediaExport.WriteAsync(Response.Body, fileName, CurrentUserId, HttpContext.RequestAborted);
    }

    /// <summary>
    /// "Database and media": the database export (recorded by <see cref="DatabaseTransferService"/>) and the media
    /// export in one .zip - the export file unchanged at the root, the files under <c>media/</c>.
    /// </summary>
    [HttpPost("export-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportAll(string? database)
    {
        // ExportAsync writes the TransferExported audit entry of the database part.
        var (file, error) = await _transfer.ExportAsync(CurrentUserId, database, HttpContext.RequestAborted);
        if (file is null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        await using (file)
        {
            var fileName = MediaExportService.FileName(DateTime.UtcNow, withDatabase: true);
            StartZipDownload(fileName);
            await _mediaExport.WriteAsync(Response.Body, fileName, CurrentUserId, HttpContext.RequestAborted, file);
        }

        return new EmptyResult();
    }

    private void StartZipDownload(string fileName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = disposition.ToString();
    }

    [HttpPost("backups")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Backup()
    {
        // CreateBackupAsync writes the BackupCreated audit entry.
        var outcome = await _transfer.CreateBackupAsync(CurrentUserId, automatic: false, HttpContext.RequestAborted);
        if (outcome.Succeeded)
        {
            TempData["Success"] = T["Backup stored: {0} ({1} records).", outcome.Record!.FileName, outcome.Record.RecordCount.ToString("N0")];
        }
        else
        {
            TempData["Error"] = outcome.Error;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("backups/schedule")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Schedule(bool enabled, int intervalHours, int keep)
    {
        // SaveScheduleAsync writes the SettingsChanged audit entry.
        var error = await _transfer.SaveScheduleAsync(
            new DatabaseTransferService.BackupSchedule(enabled, intervalHours, keep), HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View(nameof(Index), await BuildAsync());
        }

        TempData["Success"] = enabled ? T["Automatic backups every {0}, keeping {1}.", Describe(T, intervalHours), keep] : T["Automatic backups are off."];
        return RedirectToAction(nameof(Index));
    }

    /// <summary>A POST, so downloading a backup is protected by antiforgery and audited like an export.</summary>
    [HttpPost("backups/{id:guid}/download")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Download(Guid id)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        if (await _db.TransferRecords.AsNoTracking().Where(r => r.Id == id).Select(r => r.FileName).FirstOrDefaultAsync() is { } name)
        {
            disposition.SetHttpFileName(name);
        }

        // OpenBackupAsync writes the BackupDownloaded audit entry.
        if (await _transfer.OpenBackupAsync(id, disposition.ToString(), HttpContext.RequestAborted) is not { } backup)
        {
            TempData["Error"] = T["That backup no longer exists."];
            return RedirectToAction(nameof(Index));
        }

        if (backup.Url is not null)
        {
            return Redirect(backup.Url.AbsoluteUri);
        }

        return File(backup.File!.Content, "application/zip", backup.Backup.FileName);
    }

    [HttpPost("backups/{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        // DeleteBackupAsync writes the BackupDeleted audit entry.
        if (await _transfer.DeleteBackupAsync(id, HttpContext.RequestAborted))
        {
            TempData["Success"] = T["Backup deleted."];
        }
        else
        {
            TempData["Error"] = T["That backup no longer exists."];
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("backups/{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id)
    {
        if (!_transfer.ImportAllowed)
        {
            return NotFound();
        }

        // RestoreAsync writes the BackupRestored audit entry (also when refused).
        return await FinishReplaceAsync(await _transfer.RestoreAsync(id, CurrentUserId, HttpContext.RequestAborted), restored: true);
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxImportBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportBytes)]
    public async Task<IActionResult> Import(IFormFile? file, bool confirm)
    {
        if (!_transfer.ImportAllowed)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, T["Choose an export file (.zip)."]);
        }
        else if (!confirm)
        {
            ModelState.AddModelError(string.Empty, T["Confirm that all current data is replaced."]);
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildAsync());
        }

        await using var upload = file!.OpenReadStream();
        // ImportAsync writes the TransferImported audit entry (also when refused).
        var outcome = await _transfer.ImportAsync(upload, file.FileName, CurrentUserId, HttpContext.RequestAborted);
        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(string.Empty, outcome.Error!);
            return View(nameof(Index), await BuildAsync());
        }

        return await FinishReplaceAsync(outcome, restored: false);
    }

    /// <summary>
    /// After a replace the signed-in account may not exist any more (or have other roles): sign out, so the next
    /// sign-in uses an account of the imported data.
    /// </summary>
    private async Task<IActionResult> FinishReplaceAsync(DatabaseTransferService.TransferOutcome outcome, bool restored)
    {
        if (!outcome.Succeeded)
        {
            TempData["Error"] = outcome.Error;
            return RedirectToAction(nameof(Index));
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var file = outcome.Record!.FileName;
        var records = outcome.Record.RecordCount.ToString("N0");
        TempData["Success"] = restored
            ? T["Restored {0} ({1} records). Sign in with an account from that data. The previous data was backed up first.", file, records]
            : T["Imported {0} ({1} records). Sign in with an account from that data. The previous data was backed up first.", file, records];
        return Redirect("/login");
    }

    private async Task<DataManagementViewModel> BuildAsync()
    {
        var ct = HttpContext.RequestAborted;
        var history = await _transfer.HistoryAsync(25, ct);
        var backups = await _transfer.ListBackupsAsync(ct);
        var userIds = history.Concat(backups).Select(r => r.InitiatedById).OfType<Guid>().Distinct().ToList();
        return new DataManagementViewModel
        {
            Provider = _transfer.Provider,
            ExportDatabases = _transfer.ExportDatabases,
            ImportAllowed = _transfer.ImportAllowed,
            Schedule = await _transfer.GetScheduleAsync(ct),
            Backups = backups,
            History = history,
            Media = await _mediaExport.SummaryAsync(ct),
            Users = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct),
        };
    }

    /// <summary>"6 hours", "day", "week" - for the schedule select and toasts, in the admin language.</summary>
    public static string Describe(AdminText t, int hours) => hours switch
    {
        24 => t[AdminText.Key("day")],
        168 => t[AdminText.Key("week")],
        _ => t[AdminText.Key("{0} hours"), hours],
    };
}

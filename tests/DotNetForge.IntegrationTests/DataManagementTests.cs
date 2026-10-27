using System.IO.Compression;
using System.Net;
using System.Text;
using DotNetForge.Core.Transfer;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Data Management (.docs/pages/data-management.md): export, backups and their schedule, and the Development-only import and
/// restore - checked completely before they replace anything, all or nothing, after a safety backup.
/// </summary>
public sealed class DataManagementTests
{
    private const string Screen = "/admin/data";
    private const string Marker = "test.transfer-marker";

    [Fact]
    public async Task Only_a_super_admin_reaches_the_screen()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("admin2@example.com", "Sup3rSecret!", Roles.Admin);
        var admin = await factory.SignInAsync("admin2@example.com", "Sup3rSecret!");

        Assert.Contains("/account/denied", (await admin.GetAsync(Screen)).Headers.Location?.OriginalString);
        Assert.Contains("/account/denied", (await admin.PostAsync(Screen + "/export", null)).Headers.Location?.OriginalString);

        var superAdmin = await factory.SignInAsync();
        var html = await superAdmin.GetStringAsync(Screen);
        Assert.Contains("Download export", html);
        Assert.Contains("Import and replace data", html);
    }

    [Fact]
    public async Task An_export_imports_back_after_a_safety_backup_and_replaces_all_data()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await SetMarkerAsync(factory, "before");

        var export = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/export", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/zip", export.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("dotnetforge-export-", export.Content.Headers.ContentDisposition?.FileNameStar ?? export.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var file = await export.Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(file)))
        {
            Assert.NotNull(zip.GetEntry("tables/User.json"));
            Assert.Null(zip.GetEntry("tables/DataProtectionKey.json")); // this site's keys are never exported
            var manifest = await new StreamReader(zip.GetEntry(TransferManifest.FileName)!.Open()).ReadToEndAsync();
            Assert.Contains("\"format\": \"dotnetforge-export\"", manifest);
        }

        // Changes after the export disappear with the import.
        await SetMarkerAsync(factory, "after");
        await factory.CreateUserAsync("late@example.com", "Sup3rSecret!", Roles.Editor);

        var imported = await ImportAsync(admin, file);
        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        Assert.Equal("/login", imported.Headers.Location?.OriginalString);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal("before", await db.Settings.Where(s => s.Key == Marker).Select(s => s.Value).SingleAsync());
            Assert.False(await db.Users.AnyAsync(u => u.Email == "late@example.com"));
            var records = await db.TransferRecords.ToListAsync();
            Assert.Contains(records, r => r.Operation == TransferOperation.Export && r.Status == TransferStatus.Success);
            Assert.Contains(records, r => r.Operation == TransferOperation.Import && r.Status == TransferStatus.Success && r.SourceProvider != null);
            var safety = Assert.Single(records, r => r.Operation == TransferOperation.Backup);
            Assert.Equal("Before import of export.zip", safety.Note);
            Assert.True(File.Exists(Path.Combine(factory.StoragePath, safety.StorageKey!)));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.TransferImported && a.Success));
        });

        // The imported Super Admin signs in again.
        var again = await factory.SignInAsync();
        Assert.Contains("Succeeded", await again.GetStringAsync(Screen));
    }

    [Fact]
    public async Task A_foreign_or_damaged_file_is_refused_and_changes_nothing()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await SetMarkerAsync(factory, "before");
        var export = await (await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/export", new Dictionary<string, string>()))
            .Content.ReadAsByteArrayAsync();
        await SetMarkerAsync(factory, "current");

        var foreign = await ImportAsync(admin, Encoding.UTF8.GetBytes("not a zip"));
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        Assert.Contains("not a .zip export", WebUtility.HtmlDecode(await foreign.Content.ReadAsStringAsync()));

        var damaged = await ImportAsync(admin, Rewrite(export, "tables/Setting.json", json => json.Replace("before", "edited")));
        Assert.Contains("doesn't match its checksum", WebUtility.HtmlDecode(await damaged.Content.ReadAsStringAsync()));

        var unconfirmed = await ImportAsync(admin, export, confirm: false);
        Assert.Contains("Confirm that all current data is replaced", await unconfirmed.Content.ReadAsStringAsync());

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal("current", await db.Settings.Where(s => s.Key == Marker).Select(s => s.Value).SingleAsync());
            Assert.Equal(2, await db.TransferRecords.CountAsync(r => r.Operation == TransferOperation.Import && r.Status == TransferStatus.Failed));
            Assert.False(await db.TransferRecords.AnyAsync(r => r.Operation == TransferOperation.Backup)); // refused before the safety backup
        });
    }

    [Fact]
    public async Task A_backup_is_stored_downloaded_restored_and_deleted()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await SetMarkerAsync(factory, "backed-up");

        Assert.Equal(HttpStatusCode.Redirect,
            (await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/backups", new Dictionary<string, string>())).StatusCode);
        Guid id = default;
        await factory.WithDbAsync(async db => id = (await db.TransferRecords.SingleAsync(r => r.Operation == TransferOperation.Backup)).Id);
        Assert.Contains("Back up now", await admin.GetStringAsync(Screen));

        var download = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/backups/{id}/download", new Dictionary<string, string>());
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        using (var zip = new ZipArchive(new MemoryStream(await download.Content.ReadAsByteArrayAsync())))
        {
            Assert.NotNull(zip.GetEntry(TransferManifest.FileName));
        }

        await SetMarkerAsync(factory, "changed");
        var restored = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/backups/{id}/restore", new Dictionary<string, string>());
        Assert.Equal("/login", restored.Headers.Location?.OriginalString);
        await factory.WithDbAsync(async db =>
            Assert.Equal("backed-up", await db.Settings.Where(s => s.Key == Marker).Select(s => s.Value).SingleAsync()));

        admin = await factory.SignInAsync();
        await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/backups/{id}/delete", new Dictionary<string, string>());
        await factory.WithDbAsync(async db =>
        {
            var record = await db.TransferRecords.SingleAsync(r => r.Id == id);
            Assert.Null(record.StorageKey);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.BackupDeleted));
        });
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, "backups", id.ToString("N") + ".zip")));
    }

    [Fact]
    public async Task The_schedule_is_saved_and_automatic_backups_are_pruned_to_the_keep_count()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var saved = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/backups/schedule", new Dictionary<string, string>
        {
            ["enabled"] = "true",
            ["intervalHours"] = "24",
            ["keep"] = "1",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var invalid = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/backups/schedule", new Dictionary<string, string>
        {
            ["enabled"] = "true",
            ["intervalHours"] = "5",
            ["keep"] = "1",
        });
        Assert.Contains("Choose how often to back up.", await invalid.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var transfer = scope.ServiceProvider.GetRequiredService<DatabaseTransferService>();
        Assert.Equal(new DatabaseTransferService.BackupSchedule(true, 24, 1), await transfer.GetScheduleAsync(default));
        Assert.True(await transfer.BackupDueAsync(default));
        Assert.True((await transfer.CreateBackupAsync(null, automatic: true, default)).Succeeded);
        Assert.False(await transfer.BackupDueAsync(default));
        Assert.True((await transfer.CreateBackupAsync(null, automatic: true, default)).Succeeded);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(2, await db.TransferRecords.CountAsync(r => r.Automatic));
            Assert.Equal(1, await db.TransferRecords.CountAsync(r => r.Automatic && r.StorageKey != null));
        });
    }

    [Fact]
    public async Task Import_and_restore_are_not_available_outside_development()
    {
        using var factory = new DotNetForgeWebFactory("Production");
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var html = await admin.GetStringAsync(Screen);
        Assert.Contains("Not available in production", html);
        Assert.DoesNotContain("Import and replace data", html);
        var export = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, Screen + "/export", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await ImportAsync(admin, await export.Content.ReadAsByteArrayAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/backups/{Guid.NewGuid()}/restore", new Dictionary<string, string>())).StatusCode);
    }

    private static Task SetMarkerAsync(DotNetForgeWebFactory factory, string value) => factory.WithDbAsync(async db =>
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Marker);
        if (row is null)
        {
            db.Settings.Add(new Setting { Key = Marker, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync();
    });

    private static async Task<HttpResponseMessage> ImportAsync(HttpClient client, byte[] file, bool confirm = true)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, Screen);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new ByteArrayContent(file), "file", "export.zip" },
        };
        if (confirm)
        {
            content.Add(new StringContent("true"), "confirm");
        }

        return await client.PostAsync(Screen + "/import", content);
    }

    /// <summary>The export with one entry's text changed (its manifest checksum left as it was).</summary>
    private static byte[] Rewrite(byte[] export, string entryName, Func<string, string> change)
    {
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(export)))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var text = new StreamReader(entry.Open()).ReadToEnd();
                using var writer = new StreamWriter(target.CreateEntry(entry.FullName).Open());
                writer.Write(entry.FullName == entryName ? change(text) : text);
            }
        }

        return output.ToArray();
    }
}

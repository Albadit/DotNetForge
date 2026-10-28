using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The Media Library settings (.docs/pages/media-settings.md) and what they do to uploads
/// (.docs/features/media-storage.md#optimization): per-kind upload limits, image optimization (resize, quality, WebP,
/// duplicates by the uploaded size), video compression queueing without ffmpeg, and the media export of Data
/// Management (.docs/pages/data-management.md#media-export).
/// </summary>
public sealed class MediaOptimizationTests
{
    private const string Library = "/admin/media";
    private const string Settings = "/admin/media-settings";

    [Fact]
    public async Task Images_are_scaled_down_re_encoded_and_optionally_stored_as_webp()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        // Defaults: optimize on, 2560 px box, quality 82, keep the format.
        var photo = Image(4000, 3000, SKEncodedImageFormat.Jpeg, 98);
        Assert.Equal(HttpStatusCode.Redirect, (await UploadAsync(admin, "photo.jpg", photo)).StatusCode);
        var stored = await SingleAsync(factory, m => m.OriginalName == "photo.jpg");
        Assert.Equal(MediaOptimization.Optimized, stored.Optimization);
        Assert.Equal(photo.Length, stored.OriginalSizeBytes);
        Assert.True(stored.SizeBytes < photo.Length);
        Assert.Equal("image/jpeg", stored.ContentType);
        using (var image = SKBitmap.Decode(await ReadAsync(factory, stored.RelativePath)))
        {
            Assert.Equal(2560, image.Width);
            Assert.Equal(1920, image.Height);
        }

        // The same upload again is a duplicate (compared by the uploaded size, not the stored one).
        var again = await UploadAsync(admin, "photo.jpg", photo);
        Assert.Contains("'photo.jpg' is already in this folder", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()));

        // A file that only looks like an image is stored as uploaded.
        var fake = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
        await UploadAsync(admin, "fake.png", fake);
        Assert.Equal(MediaOptimization.None, (await SingleAsync(factory, m => m.OriginalName == "fake.png")).Optimization);

        // WebP on: a PNG becomes WebP, with the extension in the file name and the stored key.
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(admin, MediaSettings.Default with { ConvertToWebP = true })).StatusCode);
        await UploadAsync(admin, "logo.png", Image(1200, 800, SKEncodedImageFormat.Png, 100));
        var webp = await SingleAsync(factory, m => m.OriginalName == "logo.png");
        Assert.Equal("logo.webp", webp.FileName);
        Assert.Equal("image/webp", webp.ContentType);
        Assert.EndsWith(".webp", webp.RelativePath);

        // Optimization off: stored exactly as uploaded.
        await SaveAsync(admin, MediaSettings.Default with { OptimizeImages = false });
        await UploadAsync(admin, "raw.jpg", photo);
        var raw = await SingleAsync(factory, m => m.OriginalName == "raw.jpg");
        Assert.Equal(photo.Length, raw.SizeBytes);
        Assert.Null(raw.OriginalSizeBytes);

        // The saving is in the file's Info, not on the card.
        var library = WebUtility.HtmlDecode(await admin.GetStringAsync(Library));
        Assert.Contains("% smaller", library);
        Assert.DoesNotContain("data-tooltip=\"Optimized:", library);
    }

    [Fact]
    public async Task Upload_limits_are_per_kind_and_validated()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var screen = await admin.GetStringAsync(Settings);
        Assert.Contains("Upload limits", screen);
        Assert.Contains("No ffmpeg", screen); // FFMPEG_PATH is not set in tests

        // Out of range: refused, nothing saved.
        var invalid = await SaveAsync(admin, MediaSettings.Default with { MaxImageMb = 0, ImageQuality = 20 });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var page = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        Assert.Contains("Images: the upload limit must be 1 to 2048 MB.", page);
        Assert.Contains("Image quality must be 40 to 100.", page);
        await factory.WithDbAsync(async db => Assert.False(await db.Settings.AnyAsync(s => s.Key.StartsWith("media."))));

        // Images up to 1 MB; videos keep 100 MB, so a 30 MB video passes (the old fixed limit was 25 MB for everything).
        await SaveAsync(admin, MediaSettings.Default with { MaxImageMb = 1 });
        var big = await UploadAsync(admin, "big.png", new byte[1536 * 1024]);
        Assert.Contains("'big.png' is larger than 1 MB, the limit for images.", WebUtility.HtmlDecode(await big.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await UploadAsync(admin, "clip.mp4", new byte[30 * 1024 * 1024])).StatusCode);

        var media = await admin.GetStringAsync(Library);
        Assert.Contains("data-max-image=\"1048576\"", media);
        Assert.Contains("images up to 1 MB, videos and audio up to 100 MB", media);
        // The generic Settings screen can't bypass these checks.
        var bypass = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/settings", "/admin/settings/save",
            new Dictionary<string, string> { ["key"] = "media.maxImageMb", ["value"] = "0" });
        Assert.Contains("'media.maxImageMb' is managed on Media Library", WebUtility.HtmlDecode(await bypass.Content.ReadAsStringAsync()));
        await factory.WithDbAsync(async db =>
        {
            Assert.Equal("1", (await db.Settings.SingleAsync(s => s.Key == "media.maxImageMb")).Value);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.SettingsChanged && a.EntityDisplaySnapshot == "Media Library settings"));
        });
    }

    [Fact]
    public async Task Videos_are_not_queued_without_ffmpeg()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        // Compression chosen while ffmpeg was there: saving without ffmpeg (the switch is disabled, posts only its
        // hidden false) keeps the choice for when ffmpeg is back.
        await factory.WithDbAsync(async db =>
        {
            db.Settings.Add(new Setting { Key = "media.compressVideos", Value = "true" });
            await db.SaveChangesAsync();
        });
        await SaveAsync(admin, MediaSettings.Default with { CompressVideos = false, MaxOtherMb = 30 });
        await factory.WithDbAsync(async db =>
        {
            Assert.Equal("true", (await db.Settings.SingleAsync(s => s.Key == "media.compressVideos")).Value);
            Assert.Equal("30", (await db.Settings.SingleAsync(s => s.Key == "media.maxOtherMb")).Value);
        });

        await UploadAsync(admin, "intro.mp4", new byte[2048]);
        Assert.Equal(MediaOptimization.None, (await SingleAsync(factory, m => m.OriginalName == "intro.mp4")).Optimization);

        // A file left queued (ffmpeg removed since) goes back to "as uploaded" on the next run.
        await factory.WithDbAsync(async db =>
        {
            (await db.MediaFiles.SingleAsync()).Optimization = MediaOptimization.Pending;
            await db.SaveChangesAsync();
        });
        var worker = factory.Services.GetServices<IHostedService>().OfType<VideoOptimizationService>().Single();
        Assert.False(await worker.RunNextAsync(CancellationToken.None));
        Assert.Equal(MediaOptimization.None, (await SingleAsync(factory, m => m.OriginalName == "intro.mp4")).Optimization);

        // The ffmpeg command: a list of arguments (no shell), short side capped, quality as CRF, fast start.
        var args = Ffmpeg.Arguments("in.mp4", "out.mp4", 720, 28);
        Assert.Equal("in.mp4", args[args.ToList().IndexOf("-i") + 1]);
        Assert.Equal("28", args[args.ToList().IndexOf("-crf") + 1]);
        Assert.Contains(args, a => a.StartsWith("scale=", StringComparison.Ordinal) && a.Contains("min(720,ih)"));
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public async Task Media_export_has_the_files_in_their_folders_and_a_manifest()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await DotNetForgeWebFactory.PostFormAsync(admin, Library, Library + "/folders", new Dictionary<string, string> { ["name"] = "Photos" });
        await DotNetForgeWebFactory.PostFormAsync(admin, Library, Library + "/folders", new Dictionary<string, string> { ["name"] = "Empty" });
        Guid photos = default;
        await factory.WithDbAsync(async db => photos = (await db.MediaFolders.SingleAsync(f => f.Name == "Photos")).Id);
        await UploadAsync(admin, "beach.jpg", Image(800, 600, SKEncodedImageFormat.Jpeg, 80), photos);
        await UploadAsync(admin, "notes.txt", "hello"u8.ToArray());

        Assert.Contains("formaction=\"/admin/data/export-media\"", await admin.GetStringAsync("/admin/data"));
        var response = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/data", "/admin/data/export-media", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("dotnetforge-media-", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("Photos/", names);
        Assert.Contains("Empty/", names);
        Assert.Contains("Photos/beach.jpg", names);
        Assert.Contains("notes.txt", names);
        using (var text = new StreamReader(zip.GetEntry("notes.txt")!.Open()))
        {
            Assert.Equal("hello", await text.ReadToEndAsync());
        }

        using var manifest = JsonDocument.Parse(zip.GetEntry(MediaExportService.ManifestName)!.Open());
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(2, files.Count);
        var beach = files.Single(f => f.GetProperty("fileName").GetString() == "beach.jpg");
        Assert.Equal("Photos/beach.jpg", beach.GetProperty("path").GetString());
        Assert.Equal("/Photos", beach.GetProperty("folder").GetString());
        Assert.StartsWith("/media/", beach.GetProperty("url").GetString());

        await factory.WithDbAsync(async db =>
        {
            var record = await db.TransferRecords.SingleAsync(r => r.Operation == TransferOperation.MediaExport);
            Assert.Equal(TransferStatus.Success, record.Status);
            Assert.Equal(2, record.RecordCount);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.TransferExported && a.Details == "Media: 2 files"));
        });
        Assert.Contains("Media export", await admin.GetStringAsync("/admin/data")); // in the history too
    }

    [Fact]
    public async Task Database_and_media_export_has_the_database_export_unchanged_and_the_files_under_media()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await DotNetForgeWebFactory.PostFormAsync(admin, Library, Library + "/folders", new Dictionary<string, string> { ["name"] = "Photos" });
        Guid photos = default;
        await factory.WithDbAsync(async db => photos = (await db.MediaFolders.SingleAsync(f => f.Name == "Photos")).Id);
        await UploadAsync(admin, "beach.jpg", Image(400, 300, SKEncodedImageFormat.Jpeg, 80), photos);
        await UploadAsync(admin, "notes.txt", "hello"u8.ToArray());

        var response = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/data", "/admin/data/export-all", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("dotnetforge-full-", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("media/", names);
        Assert.Contains("media/Photos/", names);
        Assert.Contains("media/Photos/beach.jpg", names);
        Assert.Contains("media/notes.txt", names);
        Assert.Contains("media/" + MediaExportService.ManifestName, names);
        var export = Assert.Single(zip.Entries, e => e.FullName.StartsWith("dotnetforge-export-", StringComparison.Ordinal) && e.FullName.EndsWith(".zip", StringComparison.Ordinal));

        // The inner database export is a complete export of its own.
        using (var inner = new ZipArchive(export.Open()))
        {
            Assert.NotEmpty(inner.Entries);
        }

        using var manifest = JsonDocument.Parse(zip.GetEntry("media/" + MediaExportService.ManifestName)!.Open());
        Assert.Equal(export.FullName, manifest.RootElement.GetProperty("database").GetString());
        var beach = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("fileName").GetString() == "beach.jpg");
        Assert.Equal("media/Photos/beach.jpg", beach.GetProperty("path").GetString());
        Assert.Equal("/Photos", beach.GetProperty("folder").GetString());

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(TransferStatus.Success, (await db.TransferRecords.SingleAsync(r => r.Operation == TransferOperation.Export)).Status);
            var media = await db.TransferRecords.SingleAsync(r => r.Operation == TransferOperation.MediaExport);
            Assert.Equal(TransferStatus.Success, media.Status);
            Assert.Equal("With " + export.FullName, media.Note);
        });
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format, int quality)
    {
        // A gradient with detail, so encoders can't make it trivially small.
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                    [SKColors.OrangeRed, SKColors.SteelBlue, SKColors.Gold], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, paint);
            var random = new Random(42);
            using var dot = new SKPaint();
            for (var i = 0; i < 4000; i++)
            {
                dot.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                canvas.DrawCircle(random.Next(width), random.Next(height), random.Next(2, 12), dot);
            }
        }

        using var data = bitmap.Encode(format, quality);
        return data.ToArray();
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, MediaSettings s) =>
        DotNetForgeWebFactory.PostFormAsync(client, Settings, Settings, new Dictionary<string, string>
        {
            ["MaxImageMb"] = s.MaxImageMb.ToString(),
            ["MaxVideoMb"] = s.MaxVideoMb.ToString(),
            ["MaxOtherMb"] = s.MaxOtherMb.ToString(),
            ["OptimizeImages"] = s.OptimizeImages ? "true" : "false",
            ["ImageMaxWidth"] = s.ImageMaxWidth.ToString(),
            ["ImageMaxHeight"] = s.ImageMaxHeight.ToString(),
            ["ImageQuality"] = s.ImageQuality.ToString(),
            ["ConvertToWebP"] = s.ConvertToWebP ? "true" : "false",
            ["CompressVideos"] = s.CompressVideos ? "true" : "false",
            ["VideoResolution"] = s.VideoResolution.ToString(),
            ["VideoCrf"] = s.VideoCrf.ToString(),
        });

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, byte[] bytes, Guid? folder = null)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, Library);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("true"), "isPublic");
        if (folder is { } f)
        {
            form.Add(new StringContent(f.ToString()), "folderId");
        }

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "files", fileName);
        return await client.PostAsync(Library + "/upload", form);
    }

    private static async Task<MediaFile> SingleAsync(DotNetForgeWebFactory factory, System.Linq.Expressions.Expression<Func<MediaFile, bool>> match)
    {
        MediaFile? file = null;
        await factory.WithDbAsync(async db => file = await db.MediaFiles.AsNoTracking().SingleAsync(match));
        return file!;
    }

    private static async Task<byte[]> ReadAsync(DotNetForgeWebFactory factory, string key)
    {
        await using var stored = await factory.Services.GetRequiredService<IFileStorage>().OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await stored!.Content.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}

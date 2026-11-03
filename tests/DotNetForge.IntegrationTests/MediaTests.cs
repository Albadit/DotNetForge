using System.Net;
using System.Net.Http.Headers;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Media upload, download and delete through the real host and <c>IFileStorage</c> (local provider in a temp
/// directory), including access control for private files (.docs/features/media-storage.md).
/// </summary>
public sealed class MediaTests
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    internal static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, string fileName, byte[] bytes, bool isPublic)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/admin/media");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        if (isPublic)
        {
            form.Add(new StringContent("true"), "isPublic");
        }

        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "files", fileName);
        return await client.PostAsync("/admin/media/upload", form);
    }

    private static async Task<MediaFile> SingleMediaAsync(DotNetForgeWebFactory factory)
    {
        MediaFile? file = null;
        await factory.WithDbAsync(async db => file = await db.MediaFiles.AsNoTracking().SingleAsync());
        return file!;
    }

    [Fact]
    public async Task Public_upload_is_stored_outside_the_app_and_downloadable_anonymously()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var upload = await UploadAsync(admin, "logo.png", PngBytes, isPublic: true);
        Assert.Equal(HttpStatusCode.Redirect, upload.StatusCode);

        var media = await SingleMediaAsync(factory);
        Assert.Equal("image/png", media.ContentType); // derived from the extension, not the request header
        Assert.Equal(PngBytes.Length, media.SizeBytes);
        Assert.DoesNotContain("logo", media.RelativePath); // keys never contain user input
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, media.RelativePath)));

        using var anonymous = factory.CreateNoRedirectClient();
        var download = await anonymous.GetAsync($"/media/{media.Id}/logo.png");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngBytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("inline", download.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task A_file_can_be_made_private_and_public_again_by_users_who_may_update_it()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "logo.png", PngBytes, isPublic: true);
        var media = await SingleMediaAsync(factory);
        using var anonymous = factory.CreateNoRedirectClient();

        // An Author may only change their own uploads.
        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");
        var refused = await SetVisibilityAsync(author, media.Id, isPublic: false);
        Assert.Contains("/account/denied", refused.Headers.Location?.OriginalString);
        Assert.True((await SingleMediaAsync(factory)).IsPublic);

        var madePrivate = await SetVisibilityAsync(admin, media.Id, isPublic: false);
        Assert.Equal(HttpStatusCode.Redirect, madePrivate.StatusCode);
        Assert.False((await SingleMediaAsync(factory)).IsPublic);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/media/{media.Id}/logo.png")).StatusCode);
        Assert.Contains("is now private", WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/media")));

        await SetVisibilityAsync(admin, media.Id, isPublic: true);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/media/{media.Id}/logo.png")).StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.MediaUpdated)));
    }

    private static async Task<HttpResponseMessage> SetVisibilityAsync(HttpClient client, Guid id, bool isPublic)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, "/admin/media");
        return await client.PostAsync($"/admin/media/visibility/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["isPublic"] = isPublic ? "true" : "false",
            ["__RequestVerificationToken"] = token,
        }));
    }

    [Fact]
    public async Task Private_file_is_hidden_from_anonymous_users_but_served_to_admins()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "report.pdf", "%PDF-1.4 test"u8.ToArray(), isPublic: false);
        var media = await SingleMediaAsync(factory);
        Assert.False(media.IsPublic);

        using var anonymous = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/media/{media.Id}")).StatusCode);

        var asAdmin = await admin.GetAsync($"/media/{media.Id}");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        Assert.Contains("no-store", asAdmin.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Unknown_media_id_is_not_found()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/media/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("page.html")]
    [InlineData("vector.svg")]
    [InlineData("script.js")]
    [InlineData("noextension")]
    public async Task Active_or_unknown_file_types_are_rejected(string fileName)
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var response = await UploadAsync(admin, fileName, "<script>alert(1)</script>"u8.ToArray(), isPublic: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form re-rendered with the error
        Assert.Contains("file type that is not allowed", await response.Content.ReadAsStringAsync());
        await factory.WithDbAsync(async db => Assert.Equal(0, await db.MediaFiles.CountAsync()));
        Assert.False(Directory.Exists(factory.StoragePath) && Directory.EnumerateFiles(factory.StoragePath, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Delete_removes_the_row_and_the_stored_object()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "notes.txt", "hello"u8.ToArray(), isPublic: true);
        var media = await SingleMediaAsync(factory);
        var storedPath = Path.Combine(factory.StoragePath, media.RelativePath);

        var delete = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/media", $"/admin/media/delete/{media.Id}",
            new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.False(File.Exists(storedPath));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/media/{media.Id}")).StatusCode);
        await factory.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MediaDeleted)));
    }

    [Fact]
    public async Task Storage_outage_gives_a_clear_error_and_no_orphaned_row()
    {
        using var baseFactory = new DotNetForgeWebFactory();
        await baseFactory.InstallAsync();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IFileStorage, UnavailableStorage>()));
        var admin = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var signIn = await DotNetForgeWebFactory.PostFormAsync(admin, "/login", "/login",
            new Dictionary<string, string>
            {
                ["email"] = DotNetForgeWebFactory.AdminEmail,
                ["password"] = DotNetForgeWebFactory.AdminPassword,
            });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);

        var response = await UploadAsync(admin, "logo.png", PngBytes, isPublic: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("could not be stored right now", await response.Content.ReadAsStringAsync());
        await baseFactory.WithDbAsync(async db => Assert.Equal(0, await db.MediaFiles.CountAsync()));
    }

    private sealed class UnavailableStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Connection refused (storage)");

        public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Connection refused (storage)");

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Connection refused (storage)");

        public Task<Uri?> GetDownloadUrlAsync(string key, string contentDisposition, TimeSpan lifetime,
            CancellationToken cancellationToken = default) => Task.FromResult<Uri?>(null);
    }

    [Fact]
    public async Task Author_cannot_delete_media_uploaded_by_someone_else()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "photo.jpg", PngBytes, isPublic: true);
        var media = await SingleMediaAsync(factory);

        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");

        var delete = await DotNetForgeWebFactory.PostFormAsync(author, "/admin/media", $"/admin/media/delete/{media.Id}",
            new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Contains("/account/denied", delete.Headers.Location?.OriginalString);
        Assert.True(File.Exists(Path.Combine(factory.StoragePath, media.RelativePath)));
    }

    [Fact]
    public async Task Rename_keeps_the_extension_and_the_old_links()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "slides.txt", "hello"u8.ToArray(), isPublic: true);
        var media = await SingleMediaAsync(factory);

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/media"));
        Assert.Contains($"data-file-rename=\"{media.Id}\"", page);
        Assert.Contains("data-file-info=", page);

        // A typed extension (the file's own or another) never changes the type; slashes are removed.
        Assert.Equal(HttpStatusCode.Redirect, (await RenameAsync(admin, media.Id, "Q3/report.pdf")).StatusCode);
        Assert.Equal("Q3report.pdf.txt", (await SingleMediaAsync(factory)).FileName);
        await RenameAsync(admin, media.Id, "  Quarterly report.TXT ");
        var renamed = await SingleMediaAsync(factory);
        Assert.Equal("Quarterly report.txt", renamed.FileName);
        Assert.Equal("text/plain", renamed.ContentType);
        Assert.Equal(media.RelativePath, renamed.RelativePath);
        Assert.Equal("slides.txt", renamed.OriginalName);
        Assert.Contains("Renamed to 'Quarterly report.txt'.", WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/media")));

        // Old and new links both work; the download carries the new name.
        using var anonymous = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/media/{media.Id}/slides.txt")).StatusCode);
        var download = await anonymous.GetAsync(MediaService.UrlFor(renamed));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("Quarterly report.txt", download.Content.Headers.ContentDisposition?.FileNameStar);

        await RenameAsync(admin, media.Id, " .txt ");
        Assert.Contains("Enter a name.", WebUtility.HtmlDecode(await admin.GetStringAsync("/admin/media")));
        await RenameAsync(admin, media.Id, new string('a', 300));
        Assert.Equal("Quarterly report.txt", (await SingleMediaAsync(factory)).FileName);

        await factory.WithDbAsync(async db => Assert.True(await db.AuditLogs.AnyAsync(a =>
            a.Action == AuditActions.MediaUpdated && a.Details == "Renamed from 'Q3report.pdf.txt'")));

        // An Author may only rename their own uploads.
        await factory.CreateUserAsync("author@example.com", "Auth0rPass1", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPass1");
        Assert.Contains("/account/denied", (await RenameAsync(author, media.Id, "mine")).Headers.Location?.OriginalString);
        Assert.Equal("Quarterly report.txt", (await SingleMediaAsync(factory)).FileName);
    }

    [Fact]
    public async Task Folders_can_be_selected_to_move_and_delete_with_the_files()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        foreach (var name in new[] { "Archive", "Old", "Full" })
        {
            await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/media", "/admin/media/folders", new Dictionary<string, string> { ["name"] = name });
        }

        Dictionary<string, Guid> folders = new();
        await factory.WithDbAsync(async db => folders = await db.MediaFolders.ToDictionaryAsync(f => f.Name, f => f.Id));
        await UploadAsync(admin, "keep.txt", "x"u8.ToArray(), isPublic: true);
        Guid file = default;
        await factory.WithDbAsync(async db => file = (await db.MediaFiles.SingleAsync()).Id);
        Assert.Contains($"name=\"folderIds\" value=\"{folders["Old"]}\"", await admin.GetStringAsync("/admin/media"));

        // Move a folder and a file into Archive in one go; a folder can't go into itself.
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/media", "/admin/media/move-selected", new Dictionary<string, string>
        {
            ["folderIds[0]"] = folders["Old"].ToString(),
            ["folderIds[1]"] = folders["Archive"].ToString(),
            ["ids[0]"] = file.ToString(),
            ["folderId"] = folders["Archive"].ToString(),
        });
        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(folders["Archive"], (await db.MediaFolders.SingleAsync(f => f.Name == "Old")).ParentId);
            Assert.Null((await db.MediaFolders.SingleAsync(f => f.Name == "Archive")).ParentId);
            Assert.Equal(folders["Archive"], (await db.MediaFiles.SingleAsync()).FolderId);
        });

        // Delete: the empty folder goes, the one with content stays and is reported.
        var deleted = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/media", "/admin/media/delete-selected", new Dictionary<string, string>
        {
            ["folderIds[0]"] = folders["Full"].ToString(),
            ["folderIds[1]"] = folders["Archive"].ToString(),
        });
        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(deleted.Headers.Location!.OriginalString));
        Assert.Contains("Deleted 1 item.", page);
        Assert.Contains("only empty folders can be deleted", page);
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.MediaFolders.AnyAsync(f => f.Name == "Full"));
            Assert.True(await db.MediaFolders.AnyAsync(f => f.Name == "Archive"));
        });
    }

    [Fact]
    public async Task The_preview_serves_text_and_pdf_from_this_site_and_lists_zip_archives()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "notes.csv", "a,b\n1,2"u8.ToArray(), isPublic: false);
        using (var buffer = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("docs/");
                await using var entry = zip.CreateEntry("docs/readme.txt").Open();
                await entry.WriteAsync("hello"u8.ToArray());
            }

            await UploadAsync(admin, "bundle.zip", buffer.ToArray(), isPublic: true);
        }

        Dictionary<string, Guid> files = new();
        await factory.WithDbAsync(async db => files = await db.MediaFiles.ToDictionaryAsync(f => f.FileName, f => f.Id));
        var page = await admin.GetStringAsync("/admin/media");
        Assert.Contains("id=\"media-preview\"", page);
        Assert.Contains($"data-preview=\"{files["notes.csv"]}\"", page);

        // Text: shown as plain text inline (a private file too); with download=true as an attachment.
        var inline = await admin.GetAsync($"/admin/media/{files["notes.csv"]}/inline");
        Assert.Equal("text/plain", inline.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", inline.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("a,b\n1,2", await inline.Content.ReadAsStringAsync());
        var download = await admin.GetAsync($"/admin/media/{files["notes.csv"]}/inline?download=true");
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("text/csv", download.Content.Headers.ContentType?.MediaType);

        var listing = System.Text.Json.JsonDocument.Parse(await admin.GetStringAsync($"/admin/media/{files["bundle.zip"]}/zip")).RootElement;
        Assert.Equal(2, listing.GetProperty("total").GetInt32());
        Assert.Contains(listing.GetProperty("entries").EnumerateArray(), e => e.GetProperty("name").GetString() == "docs/readme.txt" && e.GetProperty("size").GetInt64() == 5);

        // Not for visitors: the admin preview needs a signed-in user who may read the library.
        using var visitor = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync($"/admin/media/{files["notes.csv"]}/inline")).StatusCode);
    }

    [Fact]
    public async Task The_preview_reads_documents_on_the_server_and_only_for_people_who_may_read_the_library()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        await UploadAsync(admin, "people.csv", "name;city\nAda;London"u8.ToArray(), isPublic: false);
        await UploadAsync(admin, "broken.docx", "this is not an archive"u8.ToArray(), isPublic: true);
        await UploadAsync(admin, "notes.txt", "hello"u8.ToArray(), isPublic: true);

        Dictionary<string, Guid> files = new();
        await factory.WithDbAsync(async db => files = await db.MediaFiles.ToDictionaryAsync(f => f.FileName, f => f.Id));

        // A private file is read for the admin: data, not markup, and never cached.
        var response = await admin.GetAsync($"/admin/media/{files["people.csv"]}/document");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var preview = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("spreadsheet", preview.GetProperty("kind").GetString());
        var rows = preview.GetProperty("sheets")[0].GetProperty("rows");
        Assert.Equal("London", rows[1][1].GetString());

        // A damaged file is an answer the dialog can show, not a server error.
        var broken = System.Text.Json.JsonDocument.Parse(await admin.GetStringAsync($"/admin/media/{files["broken.docx"]}/document")).RootElement;
        Assert.Contains("couldn't be read", broken.GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/media/{files["notes.txt"]}/document")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/media/{Guid.NewGuid()}/document")).StatusCode);

        using var visitor = factory.CreateNoRedirectClient();
        Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync($"/admin/media/{files["people.csv"]}/document")).StatusCode);
    }

    [Fact]
    public async Task Pages_move_with_the_right_to_change_them_and_say_where_they_went()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        Guid about = default, blog = default;
        await factory.WithDbAsync(async db =>
        {
            var pages = await db.Pages.Where(p => p.ParentPageId == null && p.Slug != "/").Select(p => p.Id).Take(2).ToListAsync();
            about = pages[0];
            blog = pages[1];
        });

        var page = await admin.GetStringAsync("/admin/pages");
        Assert.Contains("id=\"page-move-dialog\"", page);
        Assert.Contains("data-page-move", page);

        Assert.Equal(HttpStatusCode.Redirect, (await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/move/{about}",
            new Dictionary<string, string> { ["parentId"] = blog.ToString() })).StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal(blog, await db.Pages.Where(p => p.Id == about).Select(p => p.ParentPageId).SingleAsync()));

        // Back to Root: an empty parent. And a move into its own child is refused, with nothing changed.
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/move/{blog}", new Dictionary<string, string> { ["parentId"] = about.ToString() });
        await factory.WithDbAsync(async db => Assert.Null(await db.Pages.Where(p => p.Id == blog).Select(p => p.ParentPageId).SingleAsync()));
        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/move/{about}", new Dictionary<string, string> { ["parentId"] = string.Empty });
        await factory.WithDbAsync(async db => Assert.Null(await db.Pages.Where(p => p.Id == about).Select(p => p.ParentPageId).SingleAsync()));

        await factory.CreateUserAsync("developer@example.com", DotNetForgeWebFactory.AdminPassword, "Developer");
        var developer = await factory.SignInAsync("developer@example.com");
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, "/admin/pages");
        Assert.NotEqual(HttpStatusCode.OK, (await developer.PostAsync($"/admin/pages/move/{about}", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }))).StatusCode);
        await factory.WithDbAsync(async db => Assert.Null(await db.Pages.Where(p => p.Id == about).Select(p => p.ParentPageId).SingleAsync()));
    }

    [Fact]
    public async Task The_home_page_cant_be_deleted()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        Guid home = default;
        await factory.WithDbAsync(async db =>
        {
            home = await db.Pages.Where(p => p.Slug == "/").Select(p => p.Id).SingleAsync();
        });

        var editor = await admin.GetStringAsync($"/admin/pages?selected={home}");
        Assert.DoesNotContain("deletePageForm", editor);
        Assert.DoesNotContain($"/admin/pages/delete/{home}", editor);

        // Not by a direct post either.
        var response = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/pages", $"/admin/pages/delete/{home}", new Dictionary<string, string>());
        Assert.Contains("root and can", await response.Content.ReadAsStringAsync()); // the refusal, not the form's hint
        await factory.WithDbAsync(async db => Assert.True(await db.Pages.AnyAsync(p => p.Id == home)));
    }

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid id, string name) =>
        DotNetForgeWebFactory.PostFormAsync(client, "/admin/media", $"/admin/media/{id}/rename",
            new Dictionary<string, string> { ["name"] = name });
}

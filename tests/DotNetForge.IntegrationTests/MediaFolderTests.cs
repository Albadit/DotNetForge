using System.Net;
using System.Net.Http.Headers;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Folders of the media library (.docs/pages/media.md#folders): nested folders with breadcrumbs, uploads into a folder,
/// moving files, rename and move keeping every path in sync, no folder inside itself, deleting only empty folders, and
/// the permission split (Authors create folders and move their own files; renaming needs Media/update).
/// </summary>
public sealed class MediaFolderTests
{
    private const string Library = "/admin/media";
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    [Fact]
    public async Task Folders_nest_files_go_into_them_and_paths_follow_rename_and_move()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var photos = await CreateAsync(factory, admin, "Photos");
        var year = await CreateAsync(factory, admin, "2026", photos);
        var docs = await CreateAsync(factory, admin, "Docs");

        // Names are unique per parent, ignoring case.
        await PostAsync(admin, Library, Library + "/folders", new() { ["name"] = "photos" });
        await factory.WithDbAsync(async db => Assert.Equal(3, await db.MediaFolders.CountAsync()));

        // Upload into Photos/2026: only that folder lists it; breadcrumbs lead back.
        Assert.Equal(HttpStatusCode.Redirect, (await UploadAsync(admin, "beach.png", year)).StatusCode);

        // The same file (name and size) again in the same folder is refused; another folder may hold its own copy.
        var again = await UploadAsync(admin, "beach.png", year);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("'beach.png' is already in this folder", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await UploadAsync(admin, "beach.png", docs)).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.MediaFiles.CountAsync(m => m.FolderId == year));
            var copy = await db.MediaFiles.SingleAsync(m => m.FolderId == docs);
            db.MediaFiles.Remove(copy); // keep the rest of the test about one file
            await db.SaveChangesAsync();
        });
        var inYear = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Library}?folder={year}"));
        Assert.Contains("beach.png", inYear);
        Assert.Contains($"href=\"/admin/media?folder={photos}\"", inYear);
        var top = await admin.GetStringAsync(Library);
        Assert.DoesNotContain("beach.png", top);
        // Drag and drop (media.js): the folder tiles are drop targets and can be dragged themselves.
        Assert.Contains($"data-drop-folder=\"{photos}\"", top);
        Assert.Contains($"data-drag-folder=\"{photos}\"", top);
        Assert.Contains("/js/forge/media.js", top);
        // Upload and New folder are dialogs in the markup (nothing on the page until opened), plus the folder menu's
        // Edit… dialog.
        Assert.Matches("<dialog class=\"dialog upload-dialog\"[^>]*>\\s*<section class=\"card upload-panel\"", top);
        Assert.Contains("data-upload-panel", top);
        Assert.Contains("data-upload-toggle", top);
        Assert.Contains($"data-folder-edit=\"{photos}\"", top);
        Assert.Contains("id=\"folder-dialog\"", top);
        // New folder opens a dialog holding the create form; its Create button sits in the footer.
        Assert.Contains("data-folder-new", top);
        Assert.Contains("id=\"new-folder-dialog\"", top);
        Assert.Contains("<form method=\"post\" action=\"/admin/media/folders\" id=\"folder-create\"", top);
        Assert.Contains("form=\"folder-create\"", top);

        // Rename Photos → Pictures: the child's path follows.
        await PostAsync(admin, $"{Library}?folder={photos}", $"{Library}/folders/{photos}/rename", new() { ["name"] = "Pictures" });
        await factory.WithDbAsync(async db =>
            Assert.Equal("/Pictures/2026", (await db.MediaFolders.SingleAsync(f => f.Id == year)).Path));

        // A folder can't go into itself or below itself; elsewhere it can.
        await PostAsync(admin, $"{Library}?folder={photos}", $"{Library}/folders/{photos}/move", new() { ["parentId"] = year.ToString() });
        await factory.WithDbAsync(async db => Assert.Null((await db.MediaFolders.SingleAsync(f => f.Id == photos)).ParentId));
        await PostAsync(admin, $"{Library}?folder={photos}", $"{Library}/folders/{photos}/move", new() { ["parentId"] = docs.ToString() });
        await factory.WithDbAsync(async db =>
            Assert.Equal("/Docs/Pictures/2026", (await db.MediaFolders.SingleAsync(f => f.Id == year)).Path));

        // Move the file to the top level, then delete: a non-empty folder is refused, an empty one goes.
        Guid fileId = default;
        await factory.WithDbAsync(async db => fileId = (await db.MediaFiles.SingleAsync()).Id);
        await PostAsync(admin, $"{Library}?folder={year}", $"{Library}/move-selected", new() { ["ids"] = fileId.ToString(), ["returnFolder"] = year.ToString() });
        await factory.WithDbAsync(async db => Assert.Null((await db.MediaFiles.SingleAsync()).FolderId));

        await PostAsync(admin, $"{Library}?folder={docs}", $"{Library}/folders/{docs}/delete", new());
        await factory.WithDbAsync(async db => Assert.True(await db.MediaFolders.AnyAsync(f => f.Id == docs)));
        await PostAsync(admin, $"{Library}?folder={year}", $"{Library}/folders/{year}/delete", new());
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.MediaFolders.AnyAsync(f => f.Id == year));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MediaFolderCreated));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MediaFolderUpdated));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MediaMoved));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MediaFolderDeleted));
        });

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Library}?folder={Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    public async Task Invalid_folder_names_are_refused(string name)
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await PostAsync(admin, Library, Library + "/folders", new() { ["name"] = name });
        await factory.WithDbAsync(async db => Assert.False(await db.MediaFolders.AnyAsync()));
    }

    [Fact]
    public async Task Authors_create_folders_and_move_their_own_files_but_cannot_rename_folders()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync("author@example.com", "Auth0rPassword!", Roles.Author);
        var author = await factory.SignInAsync("author@example.com", "Auth0rPassword!");

        var folder = await CreateAsync(factory, author, "Mine");
        await UploadAsync(author, "own.png", null);
        Guid fileId = default;
        await factory.WithDbAsync(async db => fileId = (await db.MediaFiles.SingleAsync()).Id);
        await PostAsync(author, Library, $"{Library}/move-selected", new() { ["ids"] = fileId.ToString(), ["folderId"] = folder.ToString() });
        await factory.WithDbAsync(async db => Assert.Equal(folder, (await db.MediaFiles.SingleAsync()).FolderId));

        var rename = await PostAsync(author, $"{Library}?folder={folder}", $"{Library}/folders/{folder}/rename", new() { ["name"] = "Other" });
        Assert.Contains("/account/denied", rename.Headers.Location?.OriginalString);
        await factory.WithDbAsync(async db => Assert.Equal("Mine", (await db.MediaFolders.SingleAsync()).Name));
    }

    private static async Task<Guid> CreateAsync(DotNetForgeWebFactory factory, HttpClient client, string name, Guid? parent = null)
    {
        var fields = new Dictionary<string, string> { ["name"] = name };
        if (parent is { } p)
        {
            fields["parentId"] = p.ToString();
        }

        var response = await PostAsync(client, parent is null ? Library : $"{Library}?folder={parent}", Library + "/folders", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Guid id = default;
        await factory.WithDbAsync(async db => id = (await db.MediaFolders.SingleAsync(f => f.Name == name && f.ParentId == parent)).Id);
        return id;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string action, Dictionary<string, string> fields) =>
        DotNetForgeWebFactory.PostFormAsync(client, page, action, fields);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string fileName, Guid? folder)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, Library);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("true"), "isPublic");
        if (folder is { } f)
        {
            form.Add(new StringContent(f.ToString()), "folderId");
        }

        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "files", fileName);
        return await client.PostAsync(Library + "/upload", form);
    }
}

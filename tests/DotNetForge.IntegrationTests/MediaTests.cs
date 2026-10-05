using System.Net;
using System.Net.Http.Headers;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
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
        var signIn = await DotNetForgeWebFactory.PostFormAsync(admin, "/account/login", "/account/login",
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
}

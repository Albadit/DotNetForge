using System.Net.Http.Headers;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>What keeps screens fast (.docs/features/security.md): versioned files stay cached, text travels compressed.</summary>
public sealed class StaticAssetTests
{
    private const string Stylesheet = "/_content/DotNetForge.UI/dnf.css";

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string? encoding = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (encoding is not null)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_versioned_file_is_kept_and_an_unversioned_one_is_not()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var versioned = await GetAsync(client, Stylesheet + "?v=hash");
        Assert.Equal("public, max-age=31536000, immutable", versioned.Headers.CacheControl?.ToString());

        var plain = await GetAsync(client, Stylesheet);
        Assert.Null(plain.Headers.CacheControl);
    }

    [Fact]
    public async Task Screens_and_api_answers_are_never_kept_for_long()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        foreach (var path in new[] { "/admin", "/admin/users", "/api/content" })
        {
            var cacheControl = (await GetAsync(admin, path)).Headers.CacheControl;
            Assert.False(cacheControl?.Extensions.Any(e => e.Name == "immutable") == true, path);
            Assert.False(cacheControl?.MaxAge > TimeSpan.FromHours(1), path);
        }
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task Text_files_travel_compressed(string encoding)
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var full = await (await GetAsync(client, Stylesheet)).Content.ReadAsByteArrayAsync();
        var compressed = await GetAsync(client, Stylesheet, encoding);

        Assert.Contains(encoding, compressed.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", compressed.Headers.Vary);
        Assert.True((await compressed.Content.ReadAsByteArrayAsync()).Length < full.Length / 2);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)] // dotnet watch injects its script into the HTML
    public async Task Screens_travel_compressed_outside_development(string environment, bool compressed)
    {
        using var factory = new DotNetForgeWebFactory(environment);
        await factory.InstallAsync();
        using var client = factory.CreateNoRedirectClient();

        var screen = await GetAsync(client, "/login", "br");

        Assert.Equal(compressed, screen.Content.Headers.ContentEncoding.Contains("br"));
    }

    [Fact]
    public async Task Uploaded_files_are_sent_as_they_are()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var upload = await MediaTests.UploadAsync(admin, "notes.txt", "hello"u8.ToArray(), isPublic: true);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, upload.StatusCode);

        var page = await admin.GetStringAsync("/admin/media");
        var url = System.Text.RegularExpressions.Regex.Match(page, "/media/[0-9a-f-]+/notes.txt").Value;
        Assert.Empty((await GetAsync(admin, url, "br")).Content.Headers.ContentEncoding);
    }
}

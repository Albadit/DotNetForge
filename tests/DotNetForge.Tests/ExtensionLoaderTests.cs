using DotNetForge.Extensions;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>Discovery, caching and cache invalidation of <see cref="ExtensionLoader"/> (.docs/features/extensions.md).</summary>
public sealed class ExtensionLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dnf-ext-" + Guid.NewGuid().ToString("N"));

    public ExtensionLoaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteManifest(string folder, string id, string type = "admin")
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dotnetforge.extension.json"), $$"""
            { "id": "{{id}}", "name": "{{id}}", "description": "d", "version": "1.0.0", "type": "{{type}}",
              "author": "a", "entryPoint": "E", "permissions": ["core.read"] }
            """);
    }

    [Fact]
    public void Finds_valid_admin_extensions_by_id_case_insensitively()
    {
        WriteManifest("admin/one", "acme.admin.one");
        WriteManifest("themes/t", "acme.theme.t", type: "theme");
        using var loader = new ExtensionLoader(new ManifestValidator(), _root);

        Assert.Equal(2, loader.Discover().Count);
        Assert.NotNull(loader.FindAdminExtension("ACME.ADMIN.ONE"));
        Assert.Null(loader.FindAdminExtension("acme.theme.t")); // not an admin extension
    }

    [Fact]
    public async Task Cache_is_invalidated_when_a_manifest_is_added()
    {
        WriteManifest("admin/one", "acme.admin.one");
        using var loader = new ExtensionLoader(new ManifestValidator(), _root);
        Assert.Single(loader.Discover());

        WriteManifest("admin/two", "acme.admin.two");

        // File-system notifications are asynchronous; wait (bounded) for the watcher to invalidate the cache.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (loader.Discover().Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(2, loader.Discover().Count);
    }

    [Fact]
    public void Missing_root_yields_no_extensions()
    {
        using var loader = new ExtensionLoader(new ManifestValidator(), Path.Combine(_root, "absent"));
        Assert.Empty(loader.Discover());
    }
}

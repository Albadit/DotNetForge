using System.Text.Json;
using DotNetForge.Extensions;
using DotNetForge.Sdk;
using DotNetForge.Shared.Manifests;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Code loading failures are reported on the extension, never thrown (.docs/features/extensions.md#loading-code), and
/// <see cref="ExtensionContext"/> reads manifest settings. Successful loading is covered end to end by the
/// integration tests (fixture extensions in tests/fixtures/extensions).
/// </summary>
public sealed class ExtensionRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dnf-runtime-" + Guid.NewGuid().ToString("N"));

    public ExtensionRuntimeTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string folder, string id, string? assembly = null, params string[] dependencies)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var manifest = new ExtensionManifest
        {
            Id = id,
            Name = id,
            Description = "d",
            Version = "1.0.0",
            Type = "plugin",
            Author = "a",
            EntryPoint = "Acme.Entry",
            Permissions = new[] { "core.read" },
            Assembly = assembly,
            Dependencies = dependencies.ToList(),
        };
        File.WriteAllText(Path.Combine(dir, ExtensionManifest.FileName), JsonSerializer.Serialize(manifest));
    }

    private ExtensionRuntime Load()
    {
        using var loader = new ExtensionLoader(new ManifestValidator(), _root);
        return ExtensionRuntime.Load(loader.Discover(), _ => null);
    }

    [Fact]
    public void Views_only_extensions_load_no_code()
    {
        Write("a", "acme.views");

        Assert.Empty(Load().Extensions);
    }

    [Fact]
    public void A_missing_assembly_is_reported_with_the_build_hint()
    {
        Write("a", "acme.code", "bin/Acme.dll");

        var loaded = Assert.Single(Load().Extensions);
        Assert.False(loaded.IsLoaded);
        Assert.Contains("'bin/Acme.dll' was not found", loaded.Error);
        Assert.Contains("dotnet build", loaded.Error);
    }

    [Fact]
    public void A_file_that_is_not_an_assembly_is_reported()
    {
        Write("a", "acme.code", "bin/Acme.dll");
        Directory.CreateDirectory(Path.Combine(_root, "a", "bin"));
        File.WriteAllText(Path.Combine(_root, "a", "bin", "Acme.dll"), "not a PE file");

        Assert.Contains("Loading 'bin/Acme.dll' failed", Assert.Single(Load().Extensions).Error);
    }

    [Fact]
    public void Missing_failed_and_circular_dependencies_are_reported()
    {
        Write("a", "acme.needs-ghost", "bin/A.dll", "acme.ghost");
        Write("b", "acme.broken", "bin/B.dll");
        Write("c", "acme.needs-broken", "bin/C.dll", "acme.broken");
        Write("d", "acme.loop-one", "bin/D.dll", "acme.loop-two");
        Write("e", "acme.loop-two", "bin/E.dll", "acme.loop-one");

        var runtime = Load();

        Assert.Contains("'acme.ghost', which is not installed", runtime.Find("acme.needs-ghost")!.Error);
        Assert.Contains("'acme.broken', whose code failed to load", runtime.Find("acme.needs-broken")!.Error);
        Assert.Contains("Circular dependency", runtime.Find("acme.loop-two")!.Error);
        // Dependencies load first.
        var order = runtime.Extensions.Select(e => e.Id).ToList();
        Assert.True(order.IndexOf("acme.broken") < order.IndexOf("acme.needs-broken"));
    }

    [Fact]
    public void Context_reads_typed_settings_and_environment_values()
    {
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>("""
            { "id": "acme.x", "type": "Widget", "settings": { "s": "text", "b": true, "n": 7, "d": 1.5, "list": ["a", 2, "b"] } }
            """)!;
        var context = new ExtensionContext(manifest, _root, name => name == "SECRET" ? "value" : null);

        Assert.Equal("widget", context.Type);
        Assert.Equal("text", context.GetString("S"));
        Assert.True(context.GetBool("b"));
        Assert.Equal(7, context.GetInt64("n"));
        Assert.Equal(1.5, context.GetDouble("d"));
        Assert.Equal(7, context.GetDouble("n"));
        Assert.Equal(new[] { "a", "b" }, context.GetStringList("list"));
        Assert.Empty(context.GetStringList("missing"));
        Assert.Equal("fallback", context.GetString("missing", "fallback"));
        Assert.Equal("value", context.GetEnvironmentValue("SECRET"));
        Assert.Null(context.GetEnvironmentValue("OTHER"));
        Assert.Equal("/ext/acme.x/resources", context.PublicResourceBase);
    }
}

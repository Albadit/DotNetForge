using System.Net;
using DotNetForge.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The deployment directory must be treated as read-only (.docs/guides/deployment.md). These tests run the real
/// host through install, sign-in, uploads, downloads and deletes, and assert that nothing in the checkout (content
/// root and repository root) was created, changed or deleted.
/// </summary>
public sealed class ReadOnlyDeploymentTests
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "TestResults", "node_modules",
    };

    [Fact]
    public async Task A_full_session_writes_nothing_into_the_checkout()
    {
        using var factory = new DotNetForgeWebFactory("Production");
        // The content root is src/DotNetForge.Web; .env, extensions/ and development storage/ resolve to the repository
        // root, so watch the whole checkout.
        var contentRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var root = AppPaths.FindRepositoryRoot(contentRoot) ?? contentRoot;
        var before = Snapshot(root);

        await RunFullSessionAsync(factory);

        var after = Snapshot(root);
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k)); // nothing created or deleted
        var changed = before.Where(kv => after[kv.Key] != kv.Value).Select(kv => kv.Key).ToList();
        Assert.Empty(changed);
    }

    [Theory]
    [InlineData("DATABASE_CONNECTION_STRING", "Data Source=relative.db", "absolute path")]
    [InlineData("DATABASE_CONNECTION_STRING", "", "DATABASE_CONNECTION_STRING is required outside Development")]
    public void Production_refuses_paths_inside_the_deployment_directory(string key, string value, string expected)
    {
        using var factory = new DotNetForgeWebFactory("Production"); // sets valid values first
        var saved = new[] { key, "DATABASE_CONNECTION_STRING" }.Distinct().ToDictionary(k => k, Environment.GetEnvironmentVariable);
        if (key != "DATABASE_CONNECTION_STRING")
        {
            // The SQLite path rules are under test, also when DNF_TEST_POSTGRES points the factory at PostgreSQL.
            Environment.SetEnvironmentVariable(
                "DATABASE_CONNECTION_STRING", $"Data Source={Path.Combine(Path.GetTempPath(), "dnf.db")}");
        }

        Environment.SetEnvironmentVariable(key, value);
        try
        {
            var ex = Assert.Throws<ConfigurationException>(() =>
                EnvConfigurationLoader.Load(Path.GetTempPath(), isDevelopment: false));
            Assert.Contains(expected, ex.Message);
        }
        finally
        {
            foreach (var (name, original) in saved)
            {
                Environment.SetEnvironmentVariable(name, original);
            }
        }
    }

    private static async Task RunFullSessionAsync(DotNetForgeWebFactory factory)
    {
        using var setupClient = factory.CreateNoRedirectClient();
        var setup = await DotNetForgeWebFactory.PostFormAsync(setupClient, "/setup", "/setup", new Dictionary<string, string>
        {
            ["Email"] = DotNetForgeWebFactory.AdminEmail,
            ["Password"] = DotNetForgeWebFactory.AdminPassword,
            ["ConfirmPassword"] = DotNetForgeWebFactory.AdminPassword,
        });
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);

        var admin = await factory.SignInAsync();
        foreach (var path in new[] { "/admin", "/admin/content", "/admin/media", "/admin/plugins", "/", "/about/team",
                     "/admin/ext/dotnetforge.admin.audit-dashboard/raw" })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Redirect,
            (await MediaTests.UploadAsync(admin, "a.png", new byte[] { 1, 2, 3 }, isPublic: true)).StatusCode);
        Guid mediaId = default;
        await factory.WithDbAsync(async db => mediaId = db.MediaFiles.Single().Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/media/{mediaId}/a.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/media",
            $"/admin/media/delete/{mediaId}", new Dictionary<string, string>())).StatusCode);
    }

    /// <summary>Path → (length, last write) of every file under <paramref name="root"/>, minus build output.</summary>
    private static Dictionary<string, (long, DateTime)> Snapshot(string root)
    {
        var result = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var info = new FileInfo(file);
                result[Path.GetRelativePath(root, file)] = (info.Length, info.LastWriteTimeUtc);
            }
        }

        return result;
    }
}

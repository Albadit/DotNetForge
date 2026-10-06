using DotNetForge.Infrastructure.Configuration;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Where <c>.env</c>, <c>extensions/</c> and development <c>storage/</c> are found when the app runs from a checkout
/// (content root <c>src/DotNetForge.Web</c>) versus a published folder (.docs/features/configuration.md).
/// </summary>
[Collection(EnvironmentVariables.Collection)]
public sealed class AppPathsTests : IDisposable
{
    private static readonly string[] Keys = { "DATABASE_CONNECTION_STRING", "EXTENSIONS_PATH", "STORAGE_S3_BUCKET" };

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "dnf-paths-" + Guid.NewGuid().ToString("N"));
    private readonly string _contentRoot;
    private readonly Dictionary<string, string?> _saved;

    public AppPathsTests()
    {
        _contentRoot = Path.Combine(_repo, "src", "DotNetForge.Web");
        Directory.CreateDirectory(_contentRoot);
        _saved = Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        foreach (var key in Keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in _saved)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        Directory.Delete(_repo, recursive: true);
    }

    private void MarkAsCheckout() => File.WriteAllText(Path.Combine(_repo, AppPaths.SolutionFileName), "<Solution />");

    [Fact]
    public void Finds_the_repository_root_from_the_web_project()
    {
        MarkAsCheckout();
        Assert.Equal(_repo, AppPaths.FindRepositoryRoot(_contentRoot));
    }

    [Fact]
    public void A_published_folder_has_no_repository_root()
    {
        Assert.Null(AppPaths.FindRepositoryRoot(_contentRoot));
    }

    [Fact]
    public void Checkout_reads_env_and_extensions_from_the_repository_root_and_keeps_dev_data_there()
    {
        MarkAsCheckout();
        Directory.CreateDirectory(Path.Combine(_repo, "extensions"));
        File.WriteAllText(Path.Combine(_repo, ".env"), "APP_NAME=From repo root\n");

        var env = EnvConfigurationLoader.Load(_contentRoot);

        Assert.Equal("From repo root", env.AppName);
        Assert.Equal(Path.Combine(_repo, "extensions"), env.ExtensionsPath);
        Assert.Equal(_repo, AppPaths.DevelopmentDataRoot(_contentRoot)); // where the SQLite provider puts storage/dotnetforge.db
        Assert.Equal(Path.Combine(_repo, "storage", "media"), env.Storage.LocalPath);
    }

    [Fact]
    public void Files_next_to_the_app_win_over_the_repository_root()
    {
        MarkAsCheckout();
        Directory.CreateDirectory(Path.Combine(_repo, "extensions"));
        Directory.CreateDirectory(Path.Combine(_contentRoot, "extensions"));
        File.WriteAllText(Path.Combine(_contentRoot, ".env"), "APP_NAME=Local\n");

        var env = EnvConfigurationLoader.Load(_contentRoot);

        Assert.Equal("Local", env.AppName);
        Assert.Equal(Path.Combine(_contentRoot, "extensions"), env.ExtensionsPath);
    }

    [Fact]
    public void Extensions_path_can_be_configured()
    {
        var custom = Path.Combine(_repo, "custom-extensions");
        Environment.SetEnvironmentVariable("EXTENSIONS_PATH", custom);

        Assert.Equal(custom, EnvConfigurationLoader.Load(_contentRoot).ExtensionsPath);
    }
}

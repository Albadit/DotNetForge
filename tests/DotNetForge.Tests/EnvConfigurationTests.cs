using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Enums;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Validates the .env configuration contract (installation_setup.md): provider selection, the
/// PostgreSQL connection-string requirement, and fail-fast behavior on invalid/missing config.
/// Tests run serially within the class and save/restore the relevant environment variables so the
/// file-based path is exercised deterministically.
/// </summary>
public sealed class EnvConfigurationTests
{
    private static readonly string[] Keys =
    {
        "DATABASE_PROVIDER", "DATABASE_CONNECTION_STRING", "APP_NAME", "APP_URL",
        "STORAGE_PROVIDER", "STORAGE_LOCAL_PATH", "STORAGE_S3_SERVICE_URL", "STORAGE_S3_BUCKET",
        "STORAGE_S3_ACCESS_KEY_ID", "STORAGE_S3_SECRET_ACCESS_KEY", "STORAGE_S3_REGION", "STORAGE_S3_FORCE_PATH_STYLE",
    };

    private static T WithCleanEnv<T>(Func<string, T> act, string envFileContent)
    {
        var saved = Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        foreach (var k in Keys)
        {
            Environment.SetEnvironmentVariable(k, null);
        }

        var dir = Path.Combine(Path.GetTempPath(), "dnf-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            if (envFileContent is not null)
            {
                File.WriteAllText(Path.Combine(dir, ".env"), envFileContent);
            }

            return act(dir);
        }
        finally
        {
            foreach (var (k, v) in saved)
            {
                Environment.SetEnvironmentVariable(k, v);
            }

            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sqlite_default_loads()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASE_PROVIDER=sqlite\nDATABASE_CONNECTION_STRING=\nAPP_URL=http://localhost:5000\n");

        Assert.Equal(DatabaseProvider.Sqlite, env.Provider);
    }

    [Fact]
    public void Postgresql_with_connection_loads()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASE_PROVIDER=postgresql\nDATABASE_CONNECTION_STRING=Host=localhost;Database=dnf\n");

        Assert.Equal(DatabaseProvider.PostgreSql, env.Provider);
    }

    [Fact]
    public void Invalid_provider_throws()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d), "DATABASE_PROVIDER=mysql\n"));
        Assert.Contains("sqlite", ex.Message);
    }

    [Fact]
    public void Postgresql_without_connection_throws()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d), "DATABASE_PROVIDER=postgresql\nDATABASE_CONNECTION_STRING=\n"));
        Assert.Contains("DATABASE_CONNECTION_STRING", ex.Message);
    }

    [Fact]
    public void Development_defaults_stay_under_the_content_root()
    {
        var (env, dir) = WithCleanEnv(d => (EnvConfigurationLoader.Load(d), d), "DATABASE_PROVIDER=sqlite\n");

        Assert.Equal($"Data Source={Path.Combine(dir, "storage", "dotnetforge.db")}", env.ConnectionString);
        Assert.Equal(StorageProvider.Local, env.Storage.Provider);
        Assert.Equal(Path.Combine(dir, "storage", "media"), env.Storage.LocalPath);
    }

    [Fact]
    public void Production_requires_explicit_absolute_locations()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false), "DATABASE_PROVIDER=sqlite\n"));
        Assert.Contains("read-only", ex.Message);

        var absolute = Path.Combine(Path.GetTempPath(), "dnf", "cms.db");
        var media = Path.Combine(Path.GetTempPath(), "dnf", "media");
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false),
            $"DATABASE_PROVIDER=sqlite\nDATABASE_CONNECTION_STRING=Data Source={absolute}\nSTORAGE_LOCAL_PATH={media}\n");
        Assert.Equal(media, env.Storage.LocalPath);
    }

    [Fact]
    public void S3_storage_settings_load_with_r2_style_defaults()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASE_PROVIDER=sqlite\nSTORAGE_PROVIDER=s3\nSTORAGE_S3_SERVICE_URL=https://acct.r2.cloudflarestorage.com\n" +
            "STORAGE_S3_BUCKET=media\nSTORAGE_S3_ACCESS_KEY_ID=id\nSTORAGE_S3_SECRET_ACCESS_KEY=secret\n");

        Assert.Equal(StorageProvider.S3, env.Storage.Provider);
        Assert.Equal("auto", env.Storage.S3Region);
        Assert.False(env.Storage.S3ForcePathStyle);
        Assert.Equal("media", env.Storage.S3Bucket);
    }

    [Theory]
    [InlineData("STORAGE_PROVIDER=ftp\n", "STORAGE_PROVIDER")]
    [InlineData("STORAGE_PROVIDER=s3\nSTORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n", "STORAGE_S3_BUCKET")]
    [InlineData("STORAGE_PROVIDER=s3\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n", "STORAGE_S3_REGION")]
    [InlineData("STORAGE_PROVIDER=s3\nSTORAGE_S3_SERVICE_URL=not a url\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n", "absolute URL")]
    [InlineData("STORAGE_PROVIDER=s3\nSTORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\nSTORAGE_S3_FORCE_PATH_STYLE=yes\n", "true' or 'false")]
    public void Invalid_storage_configuration_throws(string storageLines, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d), "DATABASE_PROVIDER=sqlite\n" + storageLines));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Missing_env_file_throws_with_guidance()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d), null!));
        Assert.Contains(".env.example", ex.Message);
    }
}

/// <summary>The .env parser handles comments, quotes, and export prefixes.</summary>
public sealed class DotEnvParserTests
{
    [Fact]
    public void Parses_keys_comments_and_quotes()
    {
        var values = DotEnvParser.Parse(
            "# a comment\nDATABASE_PROVIDER=sqlite\n\nexport APP_NAME=\"DotNetForge CMS\"\nEMPTY=\n");

        Assert.Equal("sqlite", values["DATABASE_PROVIDER"]);
        Assert.Equal("DotNetForge CMS", values["APP_NAME"]);
        Assert.Equal(string.Empty, values["EMPTY"]);
    }
}

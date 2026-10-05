using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Enums;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Validates the .env configuration contract (.docs/features/configuration.md): the database provider detected from
/// the connection string, the read-only deployment rules, storage settings, and fail-fast behavior on invalid config.
/// Tests run serially within the class and save/restore the relevant environment variables so the
/// file-based path is exercised deterministically.
/// </summary>
[Collection(EnvironmentVariables.Collection)]
public sealed class EnvConfigurationTests
{
    private static readonly string[] Keys =
    {
        "DATABASE_CONNECTION_STRING", "APP_NAME", "APP_URL",
        "STORAGE_S3_SERVICE_URL", "STORAGE_S3_BUCKET",
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

    [Theory]
    [InlineData("DATABASE_CONNECTION_STRING=\n", DatabaseProvider.Sqlite)]
    [InlineData("APP_NAME=No database line\n", DatabaseProvider.Sqlite)]
    [InlineData("DATABASE_CONNECTION_STRING=Data Source=/data/cms.db\n", DatabaseProvider.Sqlite)]
    [InlineData("DATABASE_CONNECTION_STRING=Filename=:memory:\n", DatabaseProvider.Sqlite)]
    [InlineData("DATABASE_CONNECTION_STRING=Host=localhost;Database=dnf\n", DatabaseProvider.PostgreSql)]
    [InlineData("DATABASE_CONNECTION_STRING=server = db ; port=5432; database=dnf\n", DatabaseProvider.PostgreSql)]
    public void Provider_is_detected_from_the_connection_string(string envFile, DatabaseProvider expected)
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d), envFile);

        Assert.Equal(expected, env.Provider);
    }

    [Fact]
    public void Postgresql_connection_string_is_passed_through_unchanged()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false),
            "DATABASE_CONNECTION_STRING=Host=db;Database=dnf;Username=u;Password=p\n" +
            "STORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n");

        Assert.Equal("Host=db;Database=dnf;Username=u;Password=p", env.ConnectionString);
    }

    [Theory]
    [InlineData("DATABASE_CONNECTION_STRING=Initial Catalog=dnf;Integrated Security=true\n", "Cannot tell which database")]
    [InlineData("DATABASE_CONNECTION_STRING=postgres://u:p@db:5432/dnf\n", "key=value form")]
    public void Unrecognized_connection_string_throws(string envFile, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() => WithCleanEnv(d => EnvConfigurationLoader.Load(d), envFile));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Development_defaults_stay_under_the_content_root()
    {
        var (env, dir) = WithCleanEnv(d => (EnvConfigurationLoader.Load(d), d), "APP_NAME=Defaults\n");

        Assert.Equal($"Data Source={Path.Combine(dir, "storage", "dotnetforge.db")}", env.ConnectionString);
        Assert.Equal(StorageProvider.Local, env.Storage.Provider);
        Assert.Equal(Path.Combine(dir, "storage", "media"), env.Storage.LocalPath);
    }

    [Fact]
    public void Production_requires_explicit_absolute_locations()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false), "APP_NAME=Production\n"));
        Assert.Contains("read-only", ex.Message);

        var absolute = Path.Combine(Path.GetTempPath(), "dnf", "cms.db");
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false),
            $"DATABASE_CONNECTION_STRING=Data Source={absolute}\nSTORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n");
        Assert.Equal(StorageProvider.S3, env.Storage.Provider);
    }

    [Fact]
    public void Production_requires_s3_storage()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "dnf", "cms.db");
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false), $"DATABASE_CONNECTION_STRING=Data Source={absolute}\n"));
        Assert.Contains("File storage is not configured", ex.Message);
    }

    [Fact]
    public void Any_s3_setting_selects_s3_even_in_development()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d), "STORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_BUCKET=media\n"));
        Assert.Contains("STORAGE_S3_ACCESS_KEY_ID is required for S3 storage", ex.Message);
    }

    [Fact]
    public void S3_storage_settings_load_with_r2_style_defaults()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "STORAGE_S3_SERVICE_URL=https://acct.r2.cloudflarestorage.com\n" +
            "STORAGE_S3_BUCKET=media\nSTORAGE_S3_ACCESS_KEY_ID=id\nSTORAGE_S3_SECRET_ACCESS_KEY=secret\n");

        Assert.Equal(StorageProvider.S3, env.Storage.Provider);
        Assert.Equal("auto", env.Storage.S3Region);
        Assert.False(env.Storage.S3ForcePathStyle);
        Assert.Equal("media", env.Storage.S3Bucket);
    }

    [Theory]
    [InlineData("STORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n", "STORAGE_S3_BUCKET")]
    [InlineData("STORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n", "STORAGE_S3_REGION")]
    [InlineData("STORAGE_S3_SERVICE_URL=not a url\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n", "absolute URL")]
    [InlineData("STORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\nSTORAGE_S3_FORCE_PATH_STYLE=yes\n", "true' or 'false")]
    public void Invalid_storage_configuration_throws(string storageLines, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d), storageLines));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Missing_env_file_uses_sqlite_in_development_and_fails_clearly_elsewhere()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d), null!);
        Assert.Equal(DatabaseProvider.Sqlite, env.Provider);

        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false), null!));
        Assert.Contains("DATABASE_CONNECTION_STRING is required outside Development", ex.Message);
    }
}

/// <summary>The .env parser handles comments, quotes, and export prefixes.</summary>
public sealed class DotEnvParserTests
{
    [Fact]
    public void Parses_keys_comments_and_quotes()
    {
        var values = DotEnvParser.Parse(
            "# a comment\nAPP_URL=http://localhost:5000\n\nexport APP_NAME=\"DotNetForge CMS\"\nEMPTY=\n");

        Assert.Equal("http://localhost:5000", values["APP_URL"]);
        Assert.Equal("DotNetForge CMS", values["APP_NAME"]);
        Assert.Equal(string.Empty, values["EMPTY"]);
    }
}

using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Enums;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Validates the .env configuration contract (.docs/features/configuration.md): database settings are read as written
/// (providers validate them - see DatabaseProviderTests), named databases, storage settings, and fail-fast behavior on
/// invalid config. Tests save/restore the relevant environment variables so the file-based path is exercised
/// deterministically.
/// </summary>
[Collection(EnvironmentVariables.Collection)]
public sealed class EnvConfigurationTests
{
    private static readonly string[] Keys =
    {
        "DATABASE_PROVIDER", "DATABASE_CONNECTION_STRING", "DATABASE_NAME", "APP_NAME", "APP_URL",
        "STORAGE_S3_SERVICE_URL", "STORAGE_S3_BUCKET",
        "STORAGE_S3_ACCESS_KEY_ID", "STORAGE_S3_SECRET_ACCESS_KEY", "STORAGE_S3_REGION", "STORAGE_S3_FORCE_PATH_STYLE",
    };

    private static T WithCleanEnv<T>(Func<string, T> act, string envFileContent)
    {
        var keys = Keys.Concat(Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(k => k.StartsWith("DATABASES_", StringComparison.OrdinalIgnoreCase))).ToList();
        var saved = keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        foreach (var k in keys)
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
    public void Main_database_settings_are_read_as_written()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASE_PROVIDER=mongodb\nDATABASE_CONNECTION_STRING=mongodb://db:27017/?replicaSet=rs0\nDATABASE_NAME=cms\n");

        Assert.Equal("main", env.Database.Name);
        Assert.Equal("mongodb", env.Database.Provider);
        Assert.Equal("mongodb://db:27017/?replicaSet=rs0", env.Database.ConnectionString);
        Assert.Equal("cms", env.Database.DatabaseName);
        Assert.Empty(env.AdditionalDatabases);
    }

    [Fact]
    public void Empty_database_settings_are_left_to_the_provider_defaults()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d), "APP_NAME=Defaults\n");

        Assert.Null(env.Database.Provider);
        Assert.Null(env.Database.ConnectionString);
    }

    [Fact]
    public void Named_databases_are_read_from_DATABASES_keys()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASES_REPORTS_PROVIDER=postgresql\nDATABASES_REPORTS_CONNECTION_STRING=Host=r;Database=reports\n" +
            "DATABASES_EVENTS_CONNECTION_STRING=mongodb://e:27017/events\nDATABASES_EVENTS_NAME=events\n");

        Assert.Collection(env.AdditionalDatabases.OrderBy(d => d.Name),
            events =>
            {
                Assert.Equal("events", events.Name);
                Assert.Null(events.Provider);
                Assert.Equal("events", events.DatabaseName);
            },
            reports =>
            {
                Assert.Equal("reports", reports.Name);
                Assert.Equal("postgresql", reports.Provider);
                Assert.Equal("Host=r;Database=reports", reports.ConnectionString);
            });
    }

    [Theory]
    [InlineData("DATABASES_MAIN_PROVIDER=sqlite\n", "reserved")]
    [InlineData("DATABASES_REPORTS_PORT=5432\n", "not a valid database setting")]
    [InlineData("DATABASES__PROVIDER=sqlite\n", "not a valid database setting")]
    public void Invalid_named_database_keys_throw(string envFile, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() => WithCleanEnv(d => EnvConfigurationLoader.Load(d), envFile));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void ToString_never_contains_the_connection_string()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d), "DATABASE_PROVIDER=postgresql\nDATABASE_CONNECTION_STRING=Host=db;Password=s3cret\n");

        Assert.DoesNotContain("s3cret", env.Database.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Development_storage_defaults_stay_under_the_content_root()
    {
        var (env, dir) = WithCleanEnv(d => (EnvConfigurationLoader.Load(d), d), "APP_NAME=Defaults\n");

        Assert.Equal(StorageProvider.Local, env.Storage.Provider);
        Assert.Equal(Path.Combine(dir, "storage", "media"), env.Storage.LocalPath);
    }

    [Fact]
    public void Production_accepts_s3_storage()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d, isDevelopment: false),
            "STORAGE_S3_SERVICE_URL=https://x\nSTORAGE_S3_BUCKET=m\nSTORAGE_S3_ACCESS_KEY_ID=a\nSTORAGE_S3_SECRET_ACCESS_KEY=b\n");
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
    public void Missing_env_file_loads_development_defaults()
    {
        // Which database an empty configuration means is the providers' decision (DatabaseProviderTests).
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d), null!);

        Assert.Null(env.Database.ConnectionString);
        Assert.Equal(StorageProvider.Local, env.Storage.Provider);
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

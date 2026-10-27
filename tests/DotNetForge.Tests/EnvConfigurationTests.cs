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
        "SMTP_HOST", "SMTP_PORT", "SMTP_USERNAME", "SMTP_PASSWORD", "SMTP_FROM", "SMTP_FROM_NAME", "SMTP_TLS",
        "DOTNETFORGE_ENV_FILE",
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
    public void Email_is_off_until_smtp_is_configured()
    {
        Assert.False(WithCleanEnv(d => EnvConfigurationLoader.Load(d), "SMTP_PORT=\nSMTP_TLS=\n").Email.IsConfigured);

        var email = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "SMTP_HOST=smtp.example.com\nSMTP_FROM=cms@example.com\nSMTP_USERNAME=mailer\nSMTP_PASSWORD=secret\n").Email;
        Assert.True(email.IsConfigured);
        Assert.Equal(587, email.Port);
        Assert.True(email.UseTls);
        Assert.Equal("mailer", email.Username);

        var plain = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "SMTP_HOST=localhost\nSMTP_FROM=cms@example.com\nSMTP_PORT=1025\nSMTP_TLS=false\n").Email;
        Assert.Equal(1025, plain.Port);
        Assert.False(plain.UseTls);
    }

    [Theory]
    [InlineData("SMTP_HOST=smtp.example.com\n")]
    [InlineData("SMTP_USERNAME=mailer\nSMTP_PASSWORD=secret\n")]
    [InlineData("SMTP_HOST=smtp.example.com\nSMTP_FROM=not-an-address\n")]
    [InlineData("SMTP_HOST=smtp.example.com\nSMTP_FROM=cms@example.com\nSMTP_PORT=70000\n")]
    public void A_partial_or_invalid_smtp_configuration_fails_at_startup(string envFile)
    {
        Assert.Throws<ConfigurationException>(() => WithCleanEnv(d => EnvConfigurationLoader.Load(d), envFile));
    }

    [Fact]
    public void Main_database_settings_are_read_as_written()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASE_CONNECTION_STRING=Host=db;Database=cms\nDATABASE_PROVIDER=\n");

        Assert.Equal("main", env.Database.Name);
        Assert.Null(env.Database.Provider); // always PostgreSQL: the registry decides, nothing is read
        Assert.Equal("Host=db;Database=cms", env.Database.ConnectionString);
        Assert.Empty(env.AdditionalDatabases);
    }

    [Fact]
    public void DOTNETFORGE_ENV_FILE_replaces_the_env_file()
    {
        var other = Path.Combine(Path.GetTempPath(), $"dnf-other-{Guid.NewGuid():N}.env");
        File.WriteAllText(other, "APP_NAME=From the other file\n");
        try
        {
            var env = WithCleanEnv(d =>
            {
                Environment.SetEnvironmentVariable("DOTNETFORGE_ENV_FILE", other);
                return EnvConfigurationLoader.Load(d);
            }, "APP_NAME=From .env\n");
            Assert.Equal("From the other file", env.AppName);
        }
        finally
        {
            File.Delete(other);
        }
    }

    [Fact]
    public void A_database_provider_setting_stops_startup()
    {
        var ex = Assert.Throws<ConfigurationException>(() => WithCleanEnv(d => EnvConfigurationLoader.Load(d),
            "DATABASE_PROVIDER=postgresql\nDATABASE_CONNECTION_STRING=Host=db;Database=cms\n"));
        Assert.Contains("DATABASE_PROVIDER is no longer used", ex.Message);
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
            "DATABASES_EVENTS_CONNECTION_STRING=mongodb://e:27017/events\n");

        Assert.Collection(env.AdditionalDatabases.OrderBy(d => d.Name),
            events =>
            {
                Assert.Equal("events", events.Name);
                Assert.Null(events.Provider);
                Assert.Equal("mongodb://e:27017/events", events.ConnectionString);
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
    [InlineData("DATABASES_EVENTS_NAME=events\n", "not a valid database setting")]
    [InlineData("DATABASES__PROVIDER=sqlite\n", "not a valid database setting")]
    public void Invalid_named_database_keys_throw(string envFile, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() => WithCleanEnv(d => EnvConfigurationLoader.Load(d), envFile));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void ToString_never_contains_the_connection_string()
    {
        var env = WithCleanEnv(d => EnvConfigurationLoader.Load(d), "DATABASE_CONNECTION_STRING=Host=db;Password=s3cret\n");

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

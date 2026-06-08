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
        var env = WithCleanEnv(EnvConfigurationLoader.Load,
            "DATABASE_PROVIDER=sqlite\nDATABASE_CONNECTION_STRING=\nAPP_URL=http://localhost:5000\n");

        Assert.Equal(DatabaseProvider.Sqlite, env.Provider);
    }

    [Fact]
    public void Postgresql_with_connection_loads()
    {
        var env = WithCleanEnv(EnvConfigurationLoader.Load,
            "DATABASE_PROVIDER=postgresql\nDATABASE_CONNECTION_STRING=Host=localhost;Database=dnf\n");

        Assert.Equal(DatabaseProvider.PostgreSql, env.Provider);
    }

    [Fact]
    public void Invalid_provider_throws()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(EnvConfigurationLoader.Load, "DATABASE_PROVIDER=mysql\n"));
        Assert.Contains("sqlite", ex.Message);
    }

    [Fact]
    public void Postgresql_without_connection_throws()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(EnvConfigurationLoader.Load, "DATABASE_PROVIDER=postgresql\nDATABASE_CONNECTION_STRING=\n"));
        Assert.Contains("DATABASE_CONNECTION_STRING", ex.Message);
    }

    [Fact]
    public void Missing_env_file_throws_with_guidance()
    {
        var ex = Assert.Throws<ConfigurationException>(() =>
            WithCleanEnv(EnvConfigurationLoader.Load, null!));
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

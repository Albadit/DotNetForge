using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;

namespace DotNetForge.Infrastructure.Configuration;

/// <summary>Thrown when <c>.env</c> configuration is missing or invalid; aborts startup.</summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
}

/// <summary>
/// Loads and validates the <c>.env</c> configuration contract (installation_setup.md). Real
/// environment variables take precedence over <c>.env</c> file values. Missing or invalid
/// configuration throws <see cref="ConfigurationException"/> so the host can fail fast with a clear,
/// actionable message instead of starting in an undefined state.
/// </summary>
public static class EnvConfigurationLoader
{
    public const string ProviderKey = "DATABASE_PROVIDER";
    public const string ConnectionKey = "DATABASE_CONNECTION_STRING";
    public const string AppNameKey = "APP_NAME";
    public const string AppUrlKey = "APP_URL";

    /// <summary>
    /// Builds the typed <see cref="AppEnvironment"/> from the <c>.env</c> file in
    /// <paramref name="contentRoot"/> merged with process environment variables.
    /// </summary>
    public static AppEnvironment Load(string contentRoot)
    {
        var envPath = Path.Combine(contentRoot, ".env");
        var fileValues = File.Exists(envPath)
            ? DotEnvParser.Parse(File.ReadAllText(envPath))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? Get(string key) =>
            Environment.GetEnvironmentVariable(key) is { Length: > 0 } envVal
                ? envVal
                : fileValues.TryGetValue(key, out var fileVal) && fileVal.Length > 0 ? fileVal : null;

        var rawProvider = Get(ProviderKey);
        if (string.IsNullOrWhiteSpace(rawProvider))
        {
            if (!File.Exists(envPath))
            {
                throw new ConfigurationException(
                    "Configuration is missing. Copy '.env.example' to '.env' and set DATABASE_PROVIDER " +
                    "(sqlite or postgresql) before starting the application.");
            }

            throw new ConfigurationException(
                $"'{ProviderKey}' is required in .env and must be 'sqlite' or 'postgresql'.");
        }

        var provider = rawProvider.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgresql" or "postgres" => DatabaseProvider.PostgreSql,
            _ => throw new ConfigurationException(
                $"Invalid {ProviderKey} value '{rawProvider}'. Must be 'sqlite' or 'postgresql'."),
        };

        var connectionString = Get(ConnectionKey);
        if (provider == DatabaseProvider.PostgreSql && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ConfigurationException(
                $"{ConnectionKey} is required and must be non-empty when {ProviderKey}=postgresql.");
        }

        var appUrl = Get(AppUrlKey) ?? AppEnvironment.DefaultAppUrl;
        if (!Uri.TryCreate(appUrl, UriKind.Absolute, out _))
        {
            throw new ConfigurationException($"{AppUrlKey} must be a valid absolute URL. Got '{appUrl}'.");
        }

        return new AppEnvironment
        {
            Provider = provider,
            RawProvider = rawProvider.Trim(),
            ConnectionString = connectionString,
            AppName = Get(AppNameKey) ?? AppEnvironment.DefaultAppName,
            AppUrl = appUrl,
        };
    }
}

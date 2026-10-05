using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Enums;

namespace DotNetForge.Infrastructure.Configuration;

/// <summary>Thrown when <c>.env</c> configuration is missing or invalid; aborts startup.</summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
}

/// <summary>
/// Loads and validates the <c>.env</c> configuration contract (.docs/features/configuration.md). Real
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
    public const string StorageProviderKey = "STORAGE_PROVIDER";
    public const string StorageLocalPathKey = "STORAGE_LOCAL_PATH";
    public const string S3ServiceUrlKey = "STORAGE_S3_SERVICE_URL";
    public const string S3BucketKey = "STORAGE_S3_BUCKET";
    public const string S3AccessKeyIdKey = "STORAGE_S3_ACCESS_KEY_ID";
    public const string S3SecretAccessKeyKey = "STORAGE_S3_SECRET_ACCESS_KEY";
    public const string S3RegionKey = "STORAGE_S3_REGION";
    public const string S3ForcePathStyleKey = "STORAGE_S3_FORCE_PATH_STYLE";
    public const string ExtensionsPathKey = "EXTENSIONS_PATH";

    /// <summary>
    /// Builds the typed <see cref="AppEnvironment"/> from the <c>.env</c> file (content root, or the repository root
    /// when running from a checkout - see <see cref="AppPaths"/>) merged with process environment variables.
    /// </summary>
    /// <param name="contentRoot">The application's content root (where <c>.env</c> lives).</param>
    /// <param name="isDevelopment">
    /// Outside Development the deployment directory is treated as read-only: nothing may default to a path inside
    /// it, so the SQLite database and the local storage directory must be configured explicitly as absolute paths.
    /// </param>
    public static AppEnvironment Load(string contentRoot, bool isDevelopment = true)
    {
        var envPath = AppPaths.Resolve(contentRoot, ".env");
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

        if (provider == DatabaseProvider.Sqlite)
        {
            connectionString = ResolveSqlite(connectionString, contentRoot, isDevelopment);
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
            Storage = LoadStorage(Get, contentRoot, isDevelopment),
            ExtensionsPath = ResolveExtensionsPath(Get(ExtensionsPathKey), contentRoot),
        };
    }

    /// <summary>
    /// <c>EXTENSIONS_PATH</c> when set (absolute, or relative to the content root); otherwise <c>extensions/</c> in the
    /// content root (published app) or at the repository root (source checkout).
    /// </summary>
    private static string ResolveExtensionsPath(string? configured, string contentRoot) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? AppPaths.Resolve(contentRoot, "extensions")
            : Path.Combine(contentRoot, configured));

    private static string ResolveSqlite(string? connectionString, string contentRoot, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (!isDevelopment)
            {
                throw new ConfigurationException(
                    $"{ConnectionKey} is required outside Development when {ProviderKey}=sqlite: the deployment " +
                    "directory is read-only, so point it at a writable volume " +
                    "(e.g. 'Data Source=/data/dotnetforge.db') or use PostgreSQL.");
            }

            // Development default: storage/ at the repository root (or the content root outside a checkout), never
            // relative to the process working directory.
            return $"Data Source={Path.Combine(AppPaths.DevelopmentDataRoot(contentRoot), "storage", "dotnetforge.db")}";
        }

        var dataSource = SqliteConnectionStrings.GetDataSource(connectionString);
        if (!isDevelopment && dataSource is not null && !SqliteConnectionStrings.IsInMemory(dataSource) &&
            !Path.IsPathRooted(dataSource))
        {
            throw new ConfigurationException(
                $"The SQLite data source must be an absolute path outside Development (got '{dataSource}').");
        }

        return connectionString;
    }

    private static StorageSettings LoadStorage(Func<string, string?> get, string contentRoot, bool isDevelopment)
    {
        var raw = get(StorageProviderKey) ?? "local";
        var provider = raw.Trim().ToLowerInvariant() switch
        {
            "local" => StorageProvider.Local,
            "s3" => StorageProvider.S3,
            _ => throw new ConfigurationException(
                $"Invalid {StorageProviderKey} value '{raw}'. Must be 'local' or 's3'."),
        };

        return provider == StorageProvider.Local
            ? LoadLocalStorage(get, contentRoot, isDevelopment)
            : LoadS3Storage(get);
    }

    private static StorageSettings LoadLocalStorage(Func<string, string?> get, string contentRoot, bool isDevelopment)
    {
        var path = get(StorageLocalPathKey);
        if (string.IsNullOrWhiteSpace(path))
        {
            if (!isDevelopment)
            {
                throw new ConfigurationException(
                    $"{StorageLocalPathKey} is required outside Development when {StorageProviderKey}=local " +
                    "(an absolute path on a writable volume), or use STORAGE_PROVIDER=s3.");
            }

            path = Path.Combine(AppPaths.DevelopmentDataRoot(contentRoot), "storage", "media");
        }
        else if (!Path.IsPathRooted(path))
        {
            if (!isDevelopment)
            {
                throw new ConfigurationException(
                    $"{StorageLocalPathKey} must be an absolute path outside Development (got '{path}').");
            }

            // Same anchor as the defaults: relative to the repository root, where .env lives in a checkout.
            path = Path.Combine(AppPaths.DevelopmentDataRoot(contentRoot), path);
        }

        return new StorageSettings { Provider = StorageProvider.Local, LocalPath = Path.GetFullPath(path) };
    }

    private static StorageSettings LoadS3Storage(Func<string, string?> get)
    {
        string Require(string key) => get(key) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ConfigurationException($"{key} is required when {StorageProviderKey}=s3.");

        var serviceUrl = get(S3ServiceUrlKey)?.Trim();
        if (serviceUrl is not null && !Uri.TryCreate(serviceUrl, UriKind.Absolute, out _))
        {
            throw new ConfigurationException($"{S3ServiceUrlKey} must be an absolute URL. Got '{serviceUrl}'.");
        }

        // Custom endpoints (R2, MinIO, Supabase) accept "auto"; AWS S3 needs a real region name.
        var region = get(S3RegionKey)?.Trim() ?? (serviceUrl is null ? null : "auto");
        if (region is null)
        {
            throw new ConfigurationException(
                $"{S3RegionKey} is required when {S3ServiceUrlKey} is not set (AWS S3).");
        }

        var forcePathStyleRaw = get(S3ForcePathStyleKey);
        var forcePathStyle = false;
        if (forcePathStyleRaw is not null && !bool.TryParse(forcePathStyleRaw.Trim(), out forcePathStyle))
        {
            throw new ConfigurationException(
                $"{S3ForcePathStyleKey} must be 'true' or 'false'. Got '{forcePathStyleRaw}'.");
        }

        return new StorageSettings
        {
            Provider = StorageProvider.S3,
            S3ServiceUrl = serviceUrl,
            S3Bucket = Require(S3BucketKey),
            S3AccessKeyId = Require(S3AccessKeyIdKey),
            S3SecretAccessKey = Require(S3SecretAccessKeyKey),
            S3Region = region,
            S3ForcePathStyle = forcePathStyle,
        };
    }
}

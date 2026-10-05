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
    public const string ConnectionKey = "DATABASE_CONNECTION_STRING";
    public const string AppNameKey = "APP_NAME";
    public const string AppUrlKey = "APP_URL";
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

        var connectionString = Get(ConnectionKey);
        var provider = DetectProvider(connectionString);
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
            ConnectionString = connectionString,
            AppName = Get(AppNameKey) ?? AppEnvironment.DefaultAppName,
            AppUrl = appUrl,
            Storage = LoadStorage(Get, contentRoot, isDevelopment),
            ExtensionsPath = ResolveExtensionsPath(Get(ExtensionsPathKey), contentRoot),
        };
    }

    /// <summary>
    /// The database follows from <c>DATABASE_CONNECTION_STRING</c> (.docs/features/configuration.md#database): empty
    /// means the SQLite default, <c>Data Source=</c>/<c>Filename=</c> means SQLite, <c>Host=</c>/<c>Server=</c> means
    /// PostgreSQL. Queries are provider-neutral LINQ, but EF Core still needs the matching driver and migration set.
    /// </summary>
    private static DatabaseProvider DetectProvider(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return DatabaseProvider.Sqlite;
        }

        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"{ConnectionKey} must use the key=value form, not a URL: " +
                "'Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>'.");
        }

        var keys = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0].Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Contains("Host") || keys.Contains("Server"))
        {
            return DatabaseProvider.PostgreSql;
        }

        if (keys.Contains("Data Source") || keys.Contains("DataSource") || keys.Contains("Filename"))
        {
            return DatabaseProvider.Sqlite;
        }

        throw new ConfigurationException(
            $"Cannot tell which database {ConnectionKey} is for. Use 'Data Source=<file>' for SQLite or " +
            "'Host=<host>;Database=<db>;Username=<user>;Password=<password>' for PostgreSQL, " +
            "or leave it empty for the development SQLite database.");
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
                    $"{ConnectionKey} is required outside Development: the deployment directory is read-only, so " +
                    "point it at PostgreSQL ('Host=...;Database=...;Username=...;Password=...') or at a SQLite file on " +
                    "a writable volume ('Data Source=/data/dotnetforge.db').");
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

    /// <summary>
    /// Uploaded media lives in S3-compatible object storage (.docs/features/media-storage.md). Without any
    /// <c>STORAGE_S3_*</c> setting, Development falls back to <c>storage/media</c> at the repository root so a fresh
    /// checkout runs without a bucket; every other environment must configure S3.
    /// </summary>
    private static StorageSettings LoadStorage(Func<string, string?> get, string contentRoot, bool isDevelopment)
    {
        string[] s3Keys = { S3ServiceUrlKey, S3BucketKey, S3AccessKeyIdKey, S3SecretAccessKeyKey, S3RegionKey };
        if (s3Keys.Any(key => get(key) is not null))
        {
            return LoadS3Storage(get);
        }

        if (!isDevelopment)
        {
            throw new ConfigurationException(
                "File storage is not configured: outside Development uploaded media is stored in S3-compatible object " +
                $"storage. Set {S3BucketKey}, {S3AccessKeyIdKey} and {S3SecretAccessKeyKey}, plus {S3ServiceUrlKey} " +
                $"(Cloudflare R2, MinIO, Supabase) or {S3RegionKey} (AWS S3).");
        }

        return new StorageSettings
        {
            Provider = StorageProvider.Local,
            LocalPath = Path.Combine(AppPaths.DevelopmentDataRoot(contentRoot), "storage", "media"),
        };
    }

    private static StorageSettings LoadS3Storage(Func<string, string?> get)
    {
        string Require(string key) => get(key) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ConfigurationException($"{key} is required for S3 storage.");

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

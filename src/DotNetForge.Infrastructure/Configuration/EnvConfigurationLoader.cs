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
    public const string DatabaseNameKey = "DATABASE_NAME";
    public const string AdditionalDatabasePrefix = "DATABASES_";
    public const string AppNameKey = "APP_NAME";
    public const string AppUrlKey = "APP_URL";
    public const string S3ServiceUrlKey = "STORAGE_S3_SERVICE_URL";
    public const string S3BucketKey = "STORAGE_S3_BUCKET";
    public const string S3AccessKeyIdKey = "STORAGE_S3_ACCESS_KEY_ID";
    public const string S3SecretAccessKeyKey = "STORAGE_S3_SECRET_ACCESS_KEY";
    public const string S3RegionKey = "STORAGE_S3_REGION";
    public const string S3ForcePathStyleKey = "STORAGE_S3_FORCE_PATH_STYLE";
    public const string ExtensionsPathKey = "EXTENSIONS_PATH";

    /// <summary>Suffixes of <c>DATABASES_&lt;NAME&gt;_*</c> keys, longest first so <c>_NAME</c> doesn't shadow others.</summary>
    private static readonly string[] AdditionalDatabaseSuffixes = { "_CONNECTION_STRING", "_PROVIDER", "_NAME" };

    /// <summary>
    /// Builds the typed <see cref="AppEnvironment"/> from the <c>.env</c> file (content root, or the repository root
    /// when running from a checkout - see <see cref="AppPaths"/>) merged with process environment variables.
    /// </summary>
    /// <remarks>
    /// Database settings are read here but resolved and validated by the database providers at registration time
    /// (.docs/database/configuration.md): only they know what a valid connection string looks like.
    /// </remarks>
    /// <param name="contentRoot">The application's content root (where <c>.env</c> lives).</param>
    /// <param name="isDevelopment">
    /// Outside Development the deployment directory is treated as read-only: nothing may default to a path inside
    /// it, so media storage must be S3.
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

        var appUrl = Get(AppUrlKey) ?? AppEnvironment.DefaultAppUrl;
        if (!Uri.TryCreate(appUrl, UriKind.Absolute, out _))
        {
            throw new ConfigurationException($"{AppUrlKey} must be a valid absolute URL. Got '{appUrl}'.");
        }

        var keys = fileValues.Keys
            .Concat(Environment.GetEnvironmentVariables().Keys.Cast<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return new AppEnvironment
        {
            Database = new DatabaseSettings
            {
                Name = DatabaseSettings.MainName,
                Provider = Get(ProviderKey)?.Trim(),
                ConnectionString = Get(ConnectionKey)?.Trim(),
                DatabaseName = Get(DatabaseNameKey)?.Trim(),
            },
            AdditionalDatabases = LoadAdditionalDatabases(keys, Get),
            AppName = Get(AppNameKey) ?? AppEnvironment.DefaultAppName,
            AppUrl = appUrl,
            Storage = LoadStorage(Get, contentRoot, isDevelopment),
            ExtensionsPath = ResolveExtensionsPath(Get(ExtensionsPathKey), contentRoot),
        };
    }

    /// <summary>
    /// Named databases for <c>IDatabaseService</c>: <c>DATABASES_&lt;NAME&gt;_PROVIDER</c>,
    /// <c>DATABASES_&lt;NAME&gt;_CONNECTION_STRING</c> and <c>DATABASES_&lt;NAME&gt;_NAME</c> (.docs/database/configuration.md).
    /// </summary>
    private static IReadOnlyList<DatabaseSettings> LoadAdditionalDatabases(IEnumerable<string> keys, Func<string, string?> get)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Where(k => k.StartsWith(AdditionalDatabasePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            var rest = key[AdditionalDatabasePrefix.Length..];
            var suffix = AdditionalDatabaseSuffixes.FirstOrDefault(sfx => rest.EndsWith(sfx, StringComparison.OrdinalIgnoreCase));
            var name = suffix is null ? null : rest[..^suffix.Length];
            if (string.IsNullOrEmpty(name) || !name.All(char.IsAsciiLetterOrDigit))
            {
                throw new ConfigurationException(
                    $"'{key}' is not a valid database setting. Use {AdditionalDatabasePrefix}<NAME>_PROVIDER, " +
                    $"{AdditionalDatabasePrefix}<NAME>_CONNECTION_STRING or {AdditionalDatabasePrefix}<NAME>_NAME, " +
                    "where <NAME> has only letters and digits.");
            }

            if (name.Equals(DatabaseSettings.MainName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigurationException(
                    $"'{key}': the name '{DatabaseSettings.MainName}' is reserved for the database configured by " +
                    $"{ProviderKey} and {ConnectionKey}.");
            }

            names.Add(name);
        }

        return names.Select(name => new DatabaseSettings
        {
            Name = name.ToLowerInvariant(),
            Provider = get($"{AdditionalDatabasePrefix}{name}_PROVIDER")?.Trim(),
            ConnectionString = get($"{AdditionalDatabasePrefix}{name}_CONNECTION_STRING")?.Trim(),
            DatabaseName = get($"{AdditionalDatabasePrefix}{name}_NAME")?.Trim(),
        }).ToList();
    }

    /// <summary>
    /// <c>EXTENSIONS_PATH</c> when set (absolute, or relative to the content root); otherwise <c>extensions/</c> in the
    /// content root (published app) or at the repository root (source checkout).
    /// </summary>
    private static string ResolveExtensionsPath(string? configured, string contentRoot) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? AppPaths.Resolve(contentRoot, "extensions")
            : Path.Combine(contentRoot, configured));

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

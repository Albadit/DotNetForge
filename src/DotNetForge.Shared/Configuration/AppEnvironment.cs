using DotNetForge.Shared.Enums;

namespace DotNetForge.Shared.Configuration;

/// <summary>
/// Strongly-typed view of the <c>.env</c> configuration contract (.docs/features/configuration.md).
/// Built and validated at startup; invalid configuration must abort startup with a clear error.
/// </summary>
public sealed class AppEnvironment
{
    public const string DefaultAppName = "DotNetForge CMS";
    public const string DefaultAppUrl = "http://localhost:5000";

    /// <summary>Detected from <see cref="ConnectionString"/> by the loader (empty = SQLite).</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    public string? ConnectionString { get; set; }

    public string AppName { get; set; } = DefaultAppName;

    public string AppUrl { get; set; } = DefaultAppUrl;

    /// <summary>
    /// Absolute path of the read-only <c>extensions/</c> folder (.docs/features/extensions.md): next to the published
    /// app, or at the repository root when running from a checkout; <c>EXTENSIONS_PATH</c> overrides it.
    /// </summary>
    public string ExtensionsPath { get; set; } = string.Empty;

    /// <summary>Where uploaded media is stored (.docs/features/media-storage.md).</summary>
    public StorageSettings Storage { get; set; } = new();

    /// <summary>
    /// The effective connection string. The loader and the design-time factories always set one; the relative SQLite
    /// fallback only applies to an <see cref="AppEnvironment"/> built by hand without a connection string.
    /// </summary>
    public string ResolveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString!;
        }

        return Provider switch
        {
            DatabaseProvider.Sqlite => "Data Source=storage/dotnetforge.db",
            _ => throw new InvalidOperationException(
                "A PostgreSQL connection string is required (DATABASE_CONNECTION_STRING=Host=...)."),
        };
    }
}

/// <summary>
/// Object-storage configuration. <see cref="StorageProvider.S3"/> talks to any S3-compatible service (Cloudflare R2, AWS S3,
/// MinIO, Supabase Storage's S3 endpoint).
/// </summary>
public sealed class StorageSettings
{
    public StorageProvider Provider { get; set; } = StorageProvider.Local;

    /// <summary>Absolute directory for <see cref="StorageProvider.Local"/> (the Development fallback).</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>Custom endpoint (R2, MinIO, Supabase). Empty means AWS S3 in <see cref="S3Region"/>.</summary>
    public string? S3ServiceUrl { get; set; }

    public string S3Bucket { get; set; } = string.Empty;

    public string S3AccessKeyId { get; set; } = string.Empty;

    /// <summary>Secret - never logged or rendered.</summary>
    public string S3SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Signing region; <c>auto</c> for Cloudflare R2.</summary>
    public string S3Region { get; set; } = "auto";

    /// <summary>Use <c>endpoint/bucket/key</c> URLs instead of <c>bucket.endpoint/key</c> (MinIO, Supabase).</summary>
    public bool S3ForcePathStyle { get; set; }
}

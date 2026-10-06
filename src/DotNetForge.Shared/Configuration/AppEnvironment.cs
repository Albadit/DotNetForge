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

    /// <summary>The database the CMS runs on (<c>DATABASE_*</c>).</summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>Further named databases for <c>IDatabaseService</c> (<c>DATABASES_&lt;NAME&gt;_*</c>).</summary>
    public IReadOnlyList<DatabaseSettings> AdditionalDatabases { get; set; } = Array.Empty<DatabaseSettings>();

    public string AppName { get; set; } = DefaultAppName;

    public string AppUrl { get; set; } = DefaultAppUrl;

    /// <summary>
    /// Absolute path of the read-only <c>extensions/</c> folder (.docs/features/extensions.md): next to the published
    /// app, or at the repository root when running from a checkout; <c>EXTENSIONS_PATH</c> overrides it.
    /// </summary>
    public string ExtensionsPath { get; set; } = string.Empty;

    /// <summary>Where uploaded media is stored (.docs/features/media-storage.md).</summary>
    public StorageSettings Storage { get; set; } = new();
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

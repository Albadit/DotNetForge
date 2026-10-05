namespace DotNetForge.Shared.Entities;

/// <summary>
/// Single-row table that records whether the CMS has completed first-run installation
/// (.docs/features/installation.md). The installed flag is set atomically when setup succeeds and
/// permanently blocks the setup wizard thereafter.
/// </summary>
public class SystemState
{
    /// <summary>Fixed primary key; there is exactly one row.</summary>
    public int Id { get; set; } = 1;

    public bool Installed { get; set; }

    public DateTime? InstalledAtUtc { get; set; }

    public string CmsVersion { get; set; } = "1.0.0";
}

/// <summary>A global or per-tenant key/value setting (.docs/pages/settings.md).</summary>
public class Setting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? TenantId { get; set; }

    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }
}

/// <summary>
/// An authentication provider entry shown under Users &amp; Permissions Plugin (.docs/features/authentication.md).
/// The built-in Email provider is enabled by default; OAuth/OIDC providers are disabled until
/// configured.
/// </summary>
public class AuthProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public bool IsBuiltIn { get; set; }

    /// <summary>Provider-specific settings (client id/secret, authority), stored as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";
}

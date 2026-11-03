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

    /// <summary>
    /// Which one-time data updates the seeder has applied to existing databases (<c>DataSeeder.Upgrade</c>); 0 on
    /// databases created before the first one.
    /// </summary>
    public int SeedVersion { get; set; }
}

/// <summary>A key/value setting (.docs/pages/settings.md); keys are unique.</summary>
public class Setting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }
}

/// <summary>
/// A sign-in provider on Authentication → Providers (.docs/features/authentication.md#providers): the
/// built-in Email provider (password sign-in, always on) and one row per OAuth provider of
/// <c>OAuthProviderCatalog</c>, disabled until configured.
/// </summary>
public class AuthProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The catalog key (<c>email</c>, <c>google</c>, <c>github</c>, ...); unique.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public bool IsBuiltIn { get; set; }

    /// <summary>Provider-specific settings (client id, the protected client secret, ...), stored as JSON.</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>Last change (UTC); other app instances reload the providers when it moves.</summary>
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}

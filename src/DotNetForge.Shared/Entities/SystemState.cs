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

/// <summary>
/// An environment variable (.docs/pages/environment-variables.md): a key/value setting, for every environment
/// (<see cref="Environment"/> empty) or for one (<c>Development</c>, <c>Staging</c>, <c>Production</c>), which then wins
/// over the all-environments value in that environment. A key is unique per environment. The running app only sees the
/// rows of its own environment and of all environments (a query filter, <c>DotNetForgeDbContext</c>).
/// </summary>
public class Setting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;

    /// <summary>The value; for a <see cref="IsSecret"/> variable encrypted (Data Protection) and never shown again.</summary>
    public string? Value { get; set; }

    /// <summary>A secret (passwords, API keys, tokens): stored encrypted, write-only in the admin. Else readable config.</summary>
    public bool IsSecret { get; set; }

    /// <summary>The environment the value is for; empty = all environments.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Where to rotate it, or who to contact.</summary>
    public string? Note { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    public Guid? UpdatedById { get; set; }
}

/// <summary>
/// The environment the app runs in (<c>IHostEnvironment.EnvironmentName</c>), set once at startup: which
/// environment-specific <see cref="Setting"/> rows the app sees.
/// </summary>
public static class SettingEnvironment
{
    public static string Current { get; set; } = "Production";
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

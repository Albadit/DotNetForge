namespace DotNetForge.Shared.Enums;

/// <summary>
/// Where uploaded media is stored: <see cref="S3"/> whenever <c>STORAGE_S3_*</c> is configured; <see cref="Local"/>
/// only as the Development fallback (<c>storage/media</c>) when it is not.
/// </summary>
public enum StorageProvider
{
    Local = 0,
    S3 = 1,
}

/// <summary>Account status for a user. Disabled users cannot sign in.</summary>
public enum UserStatus
{
    Enabled = 0,
    Disabled = 1,
}

/// <summary>Tenant lifecycle state.</summary>
public enum TenantStatus
{
    Active = 0,
    Disabled = 1,
    Archived = 2,
}

/// <summary>
/// Supported extension categories. The lowercase manifest <c>type</c> value maps to these
/// (see <see cref="DotNetForge.Shared.Manifests.ExtensionManifest"/>).
/// </summary>
public enum ExtensionType
{
    Theme = 0,
    Authentication = 1,
    Connector = 2,
    Library = 3,
    Admin = 4,
    Widget = 5,
    Provider = 6,
    Plugin = 7,
    Module = 8,
}

/// <summary>Whether an installed extension is active. Extensions default to Disabled on install.</summary>
public enum ExtensionStatus
{
    Disabled = 0,
    Enabled = 1,
}

/// <summary>API token validity period.</summary>
public enum TokenDuration
{
    SevenDays = 0,
    ThirtyDays = 1,
    NinetyDays = 2,
    Unlimited = 3,
    Custom = 4,
}

/// <summary>Page kinds in the content tree.</summary>
public enum PageType
{
    Standard = 0,
    ExistingPage = 1,
    UrlRedirect = 2,
    File = 3,
}

/// <summary>Outcome of a single webhook delivery attempt.</summary>
public enum WebhookOutcome
{
    Pending = 0,
    Success = 1,
    Failure = 2,
}

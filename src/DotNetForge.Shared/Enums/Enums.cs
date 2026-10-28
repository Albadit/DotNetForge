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

/// <summary>Whether a content type holds many entries or exactly one (.docs/features/structured-content.md).</summary>
public enum ContentTypeKind
{
    Collection = 0,
    Single = 1,
}

/// <summary>What a custom API endpoint answers with (.docs/features/custom-endpoints.md).</summary>
public enum EndpointResultShape
{
    /// <summary>Every row (up to the endpoint's limit) as a list of objects.</summary>
    Rows = 0,

    /// <summary>The first row as an object; 404 when there is none.</summary>
    FirstRow = 1,

    /// <summary>The first column of the first row.</summary>
    Value = 2,

    /// <summary>The number of rows the statement changed (writes).</summary>
    AffectedRows = 3,
}

/// <summary>Who may call a custom API endpoint.</summary>
public enum EndpointAccess
{
    /// <summary>An API token with <c>endpoints.execute</c>.</summary>
    Token = 0,

    /// <summary>Anyone, without a token.</summary>
    Public = 1,
}

/// <summary>What a Data Management run did (.docs/pages/data-management.md).</summary>
public enum TransferOperation
{
    /// <summary>The data downloaded as an export file.</summary>
    Export = 0,

    /// <summary>An export kept in the file storage (manual, automatic, or the safety copy before an import).</summary>
    Backup = 1,

    /// <summary>An uploaded export replaced the data (Development only).</summary>
    Import = 2,

    /// <summary>A stored backup replaced the data (Development only).</summary>
    Restore = 3,

    /// <summary>The media files downloaded as a .zip in their library folders.</summary>
    MediaExport = 4,
}

/// <summary>Optimization state of a <see cref="Entities.MediaFile"/> (.docs/features/media-storage.md#optimization).</summary>
public enum MediaOptimization
{
    /// <summary>Stored as uploaded (not optimizable, optimization off, or the result was not smaller).</summary>
    None = 0,

    /// <summary>Replaced by a smaller version; <c>OriginalSizeBytes</c> holds the uploaded size.</summary>
    Optimized = 1,

    /// <summary>A video waiting for the background compression (ffmpeg).</summary>
    Pending = 2,

    /// <summary>A video being compressed right now.</summary>
    Processing = 3,

    /// <summary>Compression failed; the uploaded file is kept (see the log).</summary>
    Failed = 4,
}

public enum TransferStatus
{
    Success = 0,
    Failed = 1,
}

/// <summary>A second sign-in factor (.docs/features/mfa.md). Stored as int: append values, never renumber.</summary>
public enum MfaMethodType
{
    /// <summary>Time-based one-time codes from an authenticator app (TOTP, RFC 6238).</summary>
    AuthenticatorApp = 1,

    /// <summary>A one-time code emailed at sign-in (needs SMTP).</summary>
    Email = 2,

    /// <summary>A one-time code sent by text message (needs an SMS service).</summary>
    Sms = 3,

    /// <summary>A passkey or security key (WebAuthn).</summary>
    Passkey = 4,
}

/// <summary>Who must use a second factor (.docs/features/mfa.md#requirement).</summary>
public enum MfaRequirement
{
    /// <summary>Everyone may turn it on; nobody has to.</summary>
    Optional = 0,

    /// <summary>Every account that can open the admin must set one up before using it.</summary>
    AdminUsers = 1,
}

namespace DotNetForge.Shared.Constants;

/// <summary>
/// Canonical, dotted audit-log action keys (owned by .docs/features/audit-logging.md). Extensions may register
/// additional action types beyond this minimum set.
/// </summary>
public static class AuditActions
{
    public const string UserLogin = "user.login";
    public const string UserLoginFailed = "user.login.failed";
    public const string UserLogout = "user.logout";
    public const string UserCreated = "user.created";
    public const string UserRegistered = "user.registered";
    public const string PasswordResetRequested = "user.password.reset.requested";
    public const string PasswordReset = "user.password.reset";
    public const string EmailConfirmed = "user.email.confirmed";
    public const string UserUpdated = "user.updated";
    public const string UserDeleted = "user.deleted";
    public const string ContentCreated = "content.created";
    public const string ContentUpdated = "content.updated";
    public const string ContentDeleted = "content.deleted";
    public const string MediaUploaded = "media.uploaded";
    public const string MediaDeleted = "media.deleted";
    public const string MediaUpdated = "media.updated";
    public const string MediaMoved = "media.moved";
    public const string MediaFolderCreated = "media.folder.created";
    public const string MediaFolderUpdated = "media.folder.updated";
    public const string MediaFolderDeleted = "media.folder.deleted";
    public const string PluginInstalled = "plugin.installed";
    public const string PluginEnabled = "plugin.enabled";
    public const string PluginDisabled = "plugin.disabled";
    public const string RoleChanged = "role.changed";
    public const string PermissionChanged = "permission.changed";
    public const string SettingsChanged = "settings.changed";
    public const string TransferExported = "transfer.exported";
    public const string TransferImported = "transfer.imported";
    public const string BackupCreated = "transfer.backup.created";
    public const string BackupDownloaded = "transfer.backup.downloaded";
    public const string BackupDeleted = "transfer.backup.deleted";
    public const string BackupRestored = "transfer.backup.restored";
    public const string ApiTokenCreated = "apitoken.created";
    public const string ApiTokenRevoked = "apitoken.revoked";
    public const string ContentReordered = "content.reordered";
    public const string WebhookCreated = "webhook.created";
    public const string AuditExported = "audit.exported";
    public const string CmsInstalled = "cms.installed";
    public const string ContentTypeCreated = "contenttype.created";
    public const string ContentTypeUpdated = "contenttype.updated";
    public const string ContentTypeDeleted = "contenttype.deleted";
    public const string EntryCreated = "entry.created";
    public const string EntryUpdated = "entry.updated";
    public const string EntryPublished = "entry.published";
    public const string EntryUnpublished = "entry.unpublished";
    public const string EntryDeleted = "entry.deleted";
    public const string EndpointCreated = "endpoint.created";
    public const string EndpointUpdated = "endpoint.updated";
    public const string EndpointDeleted = "endpoint.deleted";
    public const string EndpointTested = "endpoint.tested";
    public const string EndpointTokenRegenerated = "endpoint.token.regenerated";

    /// <summary>A custom endpoint that is not a GET ran (it may have changed data).</summary>
    public const string EndpointExecuted = "endpoint.executed";

    /// <summary>A sign-in provider was enabled, disabled or its settings changed (Providers).</summary>
    public const string ProviderUpdated = "provider.updated";

    /// <summary>A user set up a second sign-in factor (authenticator app, email, SMS, passkey).</summary>
    public const string MfaMethodAdded = "user.mfa.added";

    /// <summary>A user removed one of their second factors.</summary>
    public const string MfaMethodRemoved = "user.mfa.removed";

    /// <summary>A user made new recovery codes (the old ones stop working).</summary>
    public const string RecoveryCodesGenerated = "user.mfa.recovery.generated";

    /// <summary>An administrator removed every second factor of a user (lost phone).</summary>
    public const string MfaReset = "user.mfa.reset";
}

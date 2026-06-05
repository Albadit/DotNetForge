namespace DotNetForge.Shared.Constants;

/// <summary>
/// Canonical, dotted audit-log action keys (owned by audit_logs.md). Extensions may register
/// additional action types beyond this minimum set.
/// </summary>
public static class AuditActions
{
    public const string UserLogin = "user.login";
    public const string UserLoginFailed = "user.login.failed";
    public const string UserLogout = "user.logout";
    public const string ContentCreated = "content.created";
    public const string ContentUpdated = "content.updated";
    public const string ContentDeleted = "content.deleted";
    public const string MediaUploaded = "media.uploaded";
    public const string PluginInstalled = "plugin.installed";
    public const string PluginDisabled = "plugin.disabled";
    public const string RoleChanged = "role.changed";
    public const string PermissionChanged = "permission.changed";
    public const string SettingsChanged = "settings.changed";
    public const string ApiTokenCreated = "apitoken.created";
    public const string ApiTokenRevoked = "apitoken.revoked";
    public const string WebhookCreated = "webhook.created";
    public const string AuditExported = "audit.exported";
    public const string CmsInstalled = "cms.installed";
}

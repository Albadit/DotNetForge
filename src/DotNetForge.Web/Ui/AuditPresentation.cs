using DotNetForge.Shared.Constants;

namespace DotNetForge.Web.Ui;

/// <summary>
/// How audit actions read in the UI (dashboard activity, Audit Logs): a sentence and an icon per known
/// <see cref="AuditActions"/> key. Unknown keys (extension-defined, e.g. <c>contactform.submitted</c>) show the raw key
/// with an icon chosen by its prefix.
/// </summary>
public static class AuditPresentation
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [AuditActions.UserLogin] = "Signed in",
        [AuditActions.UserLoginFailed] = "Failed sign-in",
        [AuditActions.UserLogout] = "Signed out",
        [AuditActions.UserCreated] = "Created a user",
        [AuditActions.UserUpdated] = "Updated a user",
        [AuditActions.UserDeleted] = "Deleted a user",
        [AuditActions.ContentCreated] = "Created a page",
        [AuditActions.ContentUpdated] = "Updated a page",
        [AuditActions.ContentDeleted] = "Deleted a page",
        [AuditActions.ContentReordered] = "Reordered pages",
        [AuditActions.MediaUploaded] = "Uploaded a file",
        [AuditActions.MediaDeleted] = "Deleted a file",
        [AuditActions.MediaUpdated] = "Changed a file",
        [AuditActions.MediaMoved] = "Moved a file",
        [AuditActions.MediaFolderCreated] = "Created a media folder",
        [AuditActions.MediaFolderUpdated] = "Changed a media folder",
        [AuditActions.MediaFolderDeleted] = "Deleted a media folder",
        [AuditActions.PluginInstalled] = "Installed an extension",
        [AuditActions.PluginEnabled] = "Enabled an extension",
        [AuditActions.PluginDisabled] = "Disabled an extension",
        [AuditActions.RoleChanged] = "Changed a role",
        [AuditActions.PermissionChanged] = "Changed permissions",
        [AuditActions.SettingsChanged] = "Changed a setting",
        [AuditActions.ApiTokenCreated] = "Created an API token",
        [AuditActions.ApiTokenRevoked] = "Revoked an API token",
        [AuditActions.WebhookCreated] = "Created a webhook",
        [AuditActions.WebhookUpdated] = "Changed a webhook",
        [AuditActions.WebhookEnabled] = "Enabled a webhook",
        [AuditActions.WebhookDisabled] = "Disabled a webhook",
        [AuditActions.WebhookDeleted] = "Deleted a webhook",
        [AuditActions.WebhookSecretRotated] = "Rotated a webhook secret",
        [AuditActions.WebhookTested] = "Sent a webhook test",
        [AuditActions.ProviderUpdated] = "Changed a sign-in provider",
        [AuditActions.MfaMethodAdded] = "Added a two-step method",
        [AuditActions.MfaMethodRemoved] = "Removed a two-step method",
        [AuditActions.RecoveryCodesGenerated] = "Made new recovery codes",
        [AuditActions.MfaReset] = "Reset two-step verification",
        [AuditActions.AuditExported] = "Exported the audit log",
        [AuditActions.CmsInstalled] = "Installed the CMS",
        [AuditActions.ContentTypeCreated] = "Created a content type",
        [AuditActions.ContentTypeUpdated] = "Changed a content type",
        [AuditActions.ContentTypeDeleted] = "Deleted a content type",
        [AuditActions.UserRegistered] = "Registered an account",
        [AuditActions.PasswordResetRequested] = "Asked for a password reset",
        [AuditActions.PasswordReset] = "Reset their password",
        [AuditActions.EmailConfirmed] = "Confirmed their email",
        [AuditActions.EntryCreated] = "Created an entry",
        [AuditActions.EntryUpdated] = "Updated an entry",
        [AuditActions.EntryPublished] = "Published an entry",
        [AuditActions.EntryUnpublished] = "Unpublished an entry",
        [AuditActions.EntryDeleted] = "Deleted an entry",
        [AuditActions.EndpointCreated] = "Created an API endpoint",
        [AuditActions.EndpointUpdated] = "Changed an API endpoint",
        [AuditActions.EndpointDeleted] = "Deleted an API endpoint",
        [AuditActions.EndpointTested] = "Test-ran an API endpoint",
        [AuditActions.EndpointTokenRegenerated] = "Regenerated an API endpoint's token",
        [AuditActions.EndpointExecuted] = "Called an API endpoint",
        [AuditActions.TransferExported] = "Exported the CMS data",
        [AuditActions.TransferImported] = "Imported CMS data",
        [AuditActions.BackupCreated] = "Created a backup",
        [AuditActions.BackupDownloaded] = "Downloaded a backup",
        [AuditActions.BackupDeleted] = "Deleted a backup",
        [AuditActions.BackupRestored] = "Restored a backup",
    };

    public static string Label(string action) => Labels.TryGetValue(action, out var label) ? label : action;

    public static string Icon(string action) => action.Split('.')[0].ToLowerInvariant() switch
    {
        "user" => "user",
        "content" => "pages",
        "contenttype" => "layers",
        "entry" => "database",
        "media" => "image",
        "plugin" => "puzzle",
        "role" or "permission" => "shield",
        "settings" => "settings",
        "apitoken" => "key",
        "endpoint" => "code",
        "webhook" => "webhook",
        "audit" => "activity",
        "cms" => "server",
        "transfer" => "transfer",
        _ => "activity",
    };
}

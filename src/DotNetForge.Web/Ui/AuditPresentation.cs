using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services.Localization;

namespace DotNetForge.Web.Ui;

/// <summary>
/// How audit actions read in the UI (dashboard activity, Audit Logs): a sentence and an icon per known
/// <see cref="AuditActions"/> key. Unknown keys (extension-defined, e.g. <c>contactform.submitted</c>) show the raw key
/// with an icon chosen by its prefix. The labels are English keys (<see cref="AdminText.Key"/>), translated where they
/// are shown (<c>@T[AuditPresentation.Label(action)]</c>).
/// </summary>
public static class AuditPresentation
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [AuditActions.UserLogin] = AdminText.Key("Signed in"),
        [AuditActions.UserLoginFailed] = AdminText.Key("Failed sign-in"),
        [AuditActions.UserLogout] = AdminText.Key("Signed out"),
        [AuditActions.UserCreated] = AdminText.Key("Created a user"),
        [AuditActions.UserUpdated] = AdminText.Key("Updated a user"),
        [AuditActions.UserDeleted] = AdminText.Key("Deleted a user"),
        [AuditActions.ContentCreated] = AdminText.Key("Created a page"),
        [AuditActions.ContentUpdated] = AdminText.Key("Updated a page"),
        [AuditActions.ContentDeleted] = AdminText.Key("Deleted a page"),
        [AuditActions.ContentReordered] = AdminText.Key("Reordered pages"),
        [AuditActions.MediaUploaded] = AdminText.Key("Uploaded a file"),
        [AuditActions.MediaDeleted] = AdminText.Key("Deleted a file"),
        [AuditActions.MediaUpdated] = AdminText.Key("Changed a file"),
        [AuditActions.MediaMoved] = AdminText.Key("Moved a file"),
        [AuditActions.MediaFolderCreated] = AdminText.Key("Created a media folder"),
        [AuditActions.MediaFolderUpdated] = AdminText.Key("Changed a media folder"),
        [AuditActions.MediaFolderDeleted] = AdminText.Key("Deleted a media folder"),
        [AuditActions.PluginInstalled] = AdminText.Key("Installed an extension"),
        [AuditActions.PluginEnabled] = AdminText.Key("Enabled an extension"),
        [AuditActions.PluginDisabled] = AdminText.Key("Disabled an extension"),
        [AuditActions.RoleChanged] = AdminText.Key("Changed a role"),
        [AuditActions.PermissionChanged] = AdminText.Key("Changed permissions"),
        [AuditActions.SettingsChanged] = AdminText.Key("Changed a setting"),
        [AuditActions.SettingsDeleted] = AdminText.Key("Deleted an environment variable"),
        [AuditActions.ApiTokenCreated] = AdminText.Key("Created an API token"),
        [AuditActions.ApiTokenRevoked] = AdminText.Key("Revoked an API token"),
        [AuditActions.WebhookCreated] = AdminText.Key("Created a webhook"),
        [AuditActions.WebhookUpdated] = AdminText.Key("Changed a webhook"),
        [AuditActions.WebhookEnabled] = AdminText.Key("Enabled a webhook"),
        [AuditActions.WebhookDisabled] = AdminText.Key("Disabled a webhook"),
        [AuditActions.WebhookDeleted] = AdminText.Key("Deleted a webhook"),
        [AuditActions.WebhookSecretRotated] = AdminText.Key("Rotated a webhook secret"),
        [AuditActions.WebhookTested] = AdminText.Key("Sent a webhook test"),
        [AuditActions.ProviderUpdated] = AdminText.Key("Changed a sign-in provider"),
        [AuditActions.MfaMethodAdded] = AdminText.Key("Added a two-step method"),
        [AuditActions.MfaMethodRemoved] = AdminText.Key("Removed a two-step method"),
        [AuditActions.RecoveryCodesGenerated] = AdminText.Key("Made new recovery codes"),
        [AuditActions.MfaReset] = AdminText.Key("Reset two-step verification"),
        [AuditActions.AuditExported] = AdminText.Key("Exported the audit log"),
        [AuditActions.CmsInstalled] = AdminText.Key("Installed the CMS"),
        [AuditActions.ContentTypeCreated] = AdminText.Key("Created a content type"),
        [AuditActions.ContentTypeUpdated] = AdminText.Key("Changed a content type"),
        [AuditActions.ContentTypeDeleted] = AdminText.Key("Deleted a content type"),
        [AuditActions.UserRegistered] = AdminText.Key("Registered an account"),
        [AuditActions.PasswordResetRequested] = AdminText.Key("Asked for a password reset"),
        [AuditActions.PasswordReset] = AdminText.Key("Reset their password"),
        [AuditActions.EmailConfirmed] = AdminText.Key("Confirmed their email"),
        [AuditActions.EntryCreated] = AdminText.Key("Created an entry"),
        [AuditActions.EntryUpdated] = AdminText.Key("Updated an entry"),
        [AuditActions.EntryPublished] = AdminText.Key("Published an entry"),
        [AuditActions.EntryUnpublished] = AdminText.Key("Unpublished an entry"),
        [AuditActions.EntryDeleted] = AdminText.Key("Deleted an entry"),
        [AuditActions.EndpointCreated] = AdminText.Key("Created an API endpoint"),
        [AuditActions.EndpointUpdated] = AdminText.Key("Changed an API endpoint"),
        [AuditActions.EndpointDeleted] = AdminText.Key("Deleted an API endpoint"),
        [AuditActions.EndpointTested] = AdminText.Key("Test-ran an API endpoint"),
        [AuditActions.EndpointTokenRegenerated] = AdminText.Key("Regenerated an API endpoint's token"),
        [AuditActions.EndpointExecuted] = AdminText.Key("Called an API endpoint"),
        [AuditActions.TransferExported] = AdminText.Key("Exported the CMS data"),
        [AuditActions.TransferImported] = AdminText.Key("Imported CMS data"),
        [AuditActions.BackupCreated] = AdminText.Key("Created a backup"),
        [AuditActions.BackupDownloaded] = AdminText.Key("Downloaded a backup"),
        [AuditActions.BackupDeleted] = AdminText.Key("Deleted a backup"),
        [AuditActions.BackupRestored] = AdminText.Key("Restored a backup"),
        [AuditActions.LocaleCreated] = AdminText.Key("Added a locale"),
        [AuditActions.LocaleUpdated] = AdminText.Key("Changed a locale"),
        [AuditActions.LocaleDeleted] = AdminText.Key("Deleted a locale"),
        [AuditActions.LocaleDefaultChanged] = AdminText.Key("Changed the default locale"),
        [AuditActions.ContentTranslated] = AdminText.Key("Translated a page"),
        [AuditActions.EntryTranslated] = AdminText.Key("Translated an entry"),
        [AuditActions.AdminLanguageChanged] = AdminText.Key("Changed the admin language"),
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
        "locale" => "globe",
        _ => "activity",
    };
}

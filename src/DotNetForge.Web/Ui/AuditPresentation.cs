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
        [AuditActions.PluginInstalled] = "Installed an extension",
        [AuditActions.PluginEnabled] = "Enabled an extension",
        [AuditActions.PluginDisabled] = "Disabled an extension",
        [AuditActions.RoleChanged] = "Changed a role",
        [AuditActions.PermissionChanged] = "Changed permissions",
        [AuditActions.SettingsChanged] = "Changed a setting",
        [AuditActions.ApiTokenCreated] = "Created an API token",
        [AuditActions.ApiTokenRevoked] = "Revoked an API token",
        [AuditActions.WebhookCreated] = "Created a webhook",
        [AuditActions.AuditExported] = "Exported the audit log",
        [AuditActions.CmsInstalled] = "Installed the CMS",
    };

    public static string Label(string action) => Labels.TryGetValue(action, out var label) ? label : action;

    public static string Icon(string action) => action.Split('.')[0].ToLowerInvariant() switch
    {
        "user" => "user",
        "content" => "pages",
        "media" => "image",
        "plugin" => "puzzle",
        "role" or "permission" => "shield",
        "settings" => "settings",
        "apitoken" => "key",
        "webhook" => "webhook",
        "audit" => "activity",
        "cms" => "server",
        _ => "activity",
    };
}

namespace DotNetForge.Web.Services.Localization.Keys;

/// <summary>
/// Texts shown through a variable on the Users, Roles and Sign-in security screens (labels defined elsewhere: the
/// permission areas, their descriptions and action names of <c>PermissionCatalog</c> in Shared, and the method names of
/// <c>MfaService.Label</c>, which also go into audit details and so stay English there), for the catalog check.
/// </summary>
internal static class AccessKeys
{
    public static readonly string[] All =
    {
        // PermissionCatalog.Areas: labels.
        AdminText.Key("Pages"),
        AdminText.Key("Content Manager"),
        AdminText.Key("Content types"),
        AdminText.Key("Single types"),
        AdminText.Key("Media"),
        AdminText.Key("Users"),
        AdminText.Key("Roles"),
        AdminText.Key("Settings"),
        AdminText.Key("API tokens"),
        AdminText.Key("Webhooks"),
        AdminText.Key("Plugins"),
        AdminText.Key("Extensions"),
        AdminText.Key("Audit logs"),
        AdminText.Key("Updates"),

        // PermissionCatalog.Areas: descriptions.
        AdminText.Key("The page tree and page layouts."),
        AdminText.Key("Entries of every content type in the Content Manager."),
        AdminText.Key("Defining content types and their fields."),
        AdminText.Key("Reserved (not checked yet)."),
        AdminText.Key("Uploaded files."),
        AdminText.Key("Admin accounts and their roles."),
        AdminText.Key("Roles and their permissions."),
        AdminText.Key("Site settings."),
        AdminText.Key("Tokens for the headless API."),
        AdminText.Key("Outgoing event notifications."),
        AdminText.Key("The Plugins screen."),
        AdminText.Key("Installing and configuring extensions."),
        AdminText.Key("The audit trail."),
        AdminText.Key("Core updates."),

        // PermissionCatalog.ActionLabel.
        AdminText.Key("Read"),
        AdminText.Key("Create"),
        AdminText.Key("Update"),
        AdminText.Key("Delete"),
        AdminText.Key("Publish"),
        AdminText.Key("Update own"),
        AdminText.Key("Delete own"),
        AdminText.Key("Manage"),
        AdminText.Key("Configure"),
        AdminText.Key("Install"),

        // MfaService.Label.
        AdminText.Key("Authenticator app"),
        AdminText.Key("Passkey"),
        AdminText.Key("Email code"),
        AdminText.Key("SMS code"),
    };
}

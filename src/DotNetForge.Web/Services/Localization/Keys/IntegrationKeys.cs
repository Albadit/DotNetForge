namespace DotNetForge.Web.Services.Localization.Keys;

/// <summary>
/// Texts shown through a variable on the API, Webhooks, Audit Logs and Dashboard screens (labels defined in Shared:
/// the permission group names of <c>PermissionKeys.GroupLabel</c> on Create API token), for the catalog check.
/// </summary>
internal static class IntegrationKeys
{
    public static readonly string[] All =
    {
        AdminText.Key("Pages"),
        AdminText.Key("Content Manager"),
        AdminText.Key("Content types"),
        AdminText.Key("Core"),
        AdminText.Key("Media"),
        AdminText.Key("Users"),
        AdminText.Key("Roles"),
        AdminText.Key("Settings"),
        AdminText.Key("Extensions"),
        AdminText.Key("Webhooks"),
    };
}

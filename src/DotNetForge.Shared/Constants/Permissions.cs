namespace DotNetForge.Shared.Constants;

/// <summary>
/// The ten canonical permission areas (owned by .docs/features/authorization.md / .docs/features/security.md).
/// Used by the RBAC check <c>IPermissionService.Has(role, area, action)</c>.
/// </summary>
public static class PermissionAreas
{
    /// <summary>Content pages (the page tree); the name predates structured content and is kept for stored grants.</summary>
    public const string CollectionTypes = "Collection types";
    public const string SingleTypes = "Single types";

    /// <summary>The content type builder: defining types and their fields (.docs/features/structured-content.md).</summary>
    public const string ContentTypes = "Content types";

    /// <summary>Entries of the content types, in the Content Manager.</summary>
    public const string Entries = "Entries";
    public const string Plugins = "Plugins";
    public const string Settings = "Settings";
    public const string Extensions = "Extensions";
    public const string Media = "Media";
    public const string Users = "Users";
    public const string Roles = "Roles";
    public const string Api = "API";
    public const string Webhooks = "Webhooks";

    // Additional capability areas used by the permission matrix (audit/update flows).
    public const string AuditLogs = "Audit logs";
    public const string Updates = "Updates";

    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        CollectionTypes, SingleTypes, Plugins, Settings, Extensions,
        Media, Users, Roles, Api, Webhooks,
    };

    public static readonly IReadOnlyList<string> All = new[]
    {
        CollectionTypes, SingleTypes, Plugins, Settings, Extensions,
        Media, Users, Roles, Api, Webhooks, AuditLogs, Updates, ContentTypes, Entries,
    };
}

/// <summary>Granular actions assigned to roles within a permission area.</summary>
public static class PermissionActions
{
    public const string Read = "read";
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Publish = "publish";
    public const string UpdateOwn = "update.own";
    public const string DeleteOwn = "delete.own";
    public const string Manage = "manage";
    public const string Configure = "configure";
    public const string Install = "install";
}

/// <summary>
/// The dotted API-token / extension permission keys (owned by .docs/features/headless-api.md). These are the
/// granular scopes an <see cref="DotNetForge.Shared.Entities.ApiToken"/> may carry and that an
/// extension manifest may request.
/// </summary>
public static class PermissionKeys
{
    public const string CoreRead = "core.read";
    public const string CoreManage = "core.manage";

    public const string ContentRead = "content.read";
    public const string ContentCreate = "content.create";
    public const string ContentUpdate = "content.update";
    public const string ContentDelete = "content.delete";
    public const string ContentPublish = "content.publish";

    public const string SchemaRead = "schema.read";

    public const string EntriesRead = "entries.read";
    public const string EntriesCreate = "entries.create";
    public const string EntriesUpdate = "entries.update";
    public const string EntriesDelete = "entries.delete";
    public const string EntriesPublish = "entries.publish";

    public const string MediaRead = "media.read";
    public const string MediaUpload = "media.upload";
    public const string MediaUpdate = "media.update";
    public const string MediaDelete = "media.delete";

    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDelete = "users.delete";

    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string RolesDelete = "roles.delete";

    public const string SettingsRead = "settings.read";
    public const string SettingsUpdate = "settings.update";

    public const string ExtensionsRead = "extensions.read";
    public const string ExtensionsManage = "extensions.manage";

    public const string WebhooksRead = "webhooks.read";
    public const string WebhooksManage = "webhooks.manage";

    /// <summary>Every first-party permission key. A Super Admin token may carry all of these.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CoreRead, CoreManage,
        ContentRead, ContentCreate, ContentUpdate, ContentDelete, ContentPublish,
        SchemaRead, EntriesRead, EntriesCreate, EntriesUpdate, EntriesDelete, EntriesPublish,
        MediaRead, MediaUpload, MediaUpdate, MediaDelete,
        UsersRead, UsersCreate, UsersUpdate, UsersDelete,
        RolesRead, RolesCreate, RolesUpdate, RolesDelete,
        SettingsRead, SettingsUpdate,
        ExtensionsRead, ExtensionsManage,
        WebhooksRead, WebhooksManage,
    };

    public static bool IsKnown(string key) => All.Contains(key);

    /// <summary>
    /// How a group of keys (their prefix) is named on screens. The stored keys keep their names - tokens hold them -
    /// so <c>content.*</c> (the page tree) shows as "Pages", <c>entries.*</c> as "Content Manager".
    /// </summary>
    public static string GroupLabel(string prefix) => prefix switch
    {
        "content" => "Pages",
        "entries" => "Content Manager",
        "schema" => "Content types",
        "core" => "Core",
        "media" => "Media",
        "users" => "Users",
        "roles" => "Roles",
        "settings" => "Settings",
        "extensions" => "Extensions",
        "webhooks" => "Webhooks",
        _ => prefix,
    };
}

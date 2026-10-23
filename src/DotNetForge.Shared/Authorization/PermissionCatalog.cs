using DotNetForge.Shared.Constants;

namespace DotNetForge.Shared.Authorization;

/// <summary>One permission area as the role editor shows it: its actions in display order.</summary>
public sealed record PermissionAreaDefinition(string Area, string Label, string Description, IReadOnlyList<string> Actions);

/// <summary>
/// The grantable <c>(area, action)</c> pairs (.docs/features/authorization.md#permission-catalog): what the role
/// editor offers, what a saved grant set may contain, and what the effective-permission views list. Areas and actions
/// are the <see cref="PermissionAreas"/> / <see cref="PermissionActions"/> constants the code checks.
/// </summary>
public static class PermissionCatalog
{
    private static readonly string[] ContentActions =
    {
        PermissionActions.Read, PermissionActions.Create, PermissionActions.Update, PermissionActions.Delete,
        PermissionActions.Publish, PermissionActions.UpdateOwn, PermissionActions.DeleteOwn,
    };

    private static readonly string[] Crud =
    {
        PermissionActions.Read, PermissionActions.Create, PermissionActions.Update, PermissionActions.Delete,
    };

    public static readonly IReadOnlyList<PermissionAreaDefinition> Areas = new[]
    {
        new PermissionAreaDefinition(PermissionAreas.CollectionTypes, "Content pages", "The page tree in the Content Manager.", ContentActions),
        new PermissionAreaDefinition(PermissionAreas.SingleTypes, "Single types", "Single content entries.", ContentActions),
        new PermissionAreaDefinition(PermissionAreas.Media, "Media", "Uploaded files.", new[]
        {
            PermissionActions.Read, PermissionActions.Create, PermissionActions.Update, PermissionActions.Delete,
            PermissionActions.UpdateOwn, PermissionActions.DeleteOwn,
        }),
        new PermissionAreaDefinition(PermissionAreas.Users, "Users", "Admin accounts and their roles.", Crud),
        new PermissionAreaDefinition(PermissionAreas.Roles, "Roles", "Roles and their permissions.", Crud),
        new PermissionAreaDefinition(PermissionAreas.Settings, "Settings", "Site settings.", new[] { PermissionActions.Read, PermissionActions.Update }),
        new PermissionAreaDefinition(PermissionAreas.Api, "API tokens", "Tokens for the headless API.", new[]
        {
            PermissionActions.Read, PermissionActions.Create, PermissionActions.Delete,
        }),
        new PermissionAreaDefinition(PermissionAreas.Webhooks, "Webhooks", "Outgoing event notifications.", Crud),
        new PermissionAreaDefinition(PermissionAreas.Plugins, "Plugins", "The Plugins screen.", new[] { PermissionActions.Read, PermissionActions.Manage }),
        new PermissionAreaDefinition(PermissionAreas.Extensions, "Extensions", "Installing and configuring extensions.", new[]
        {
            PermissionActions.Read, PermissionActions.Install, PermissionActions.Configure,
        }),
        new PermissionAreaDefinition(PermissionAreas.AuditLogs, "Audit logs", "The audit trail.", new[] { PermissionActions.Read }),
        new PermissionAreaDefinition(PermissionAreas.Updates, "Updates", "Core updates.", new[] { PermissionActions.Read, PermissionActions.Install }),
    };

    /// <summary>The storage/lookup key of a grant: <c>area|action</c>, case-insensitive by convention.</summary>
    public static string Key(string area, string action) => $"{area}|{action}";

    /// <summary>Whether <paramref name="area"/>/<paramref name="action"/> is a grantable pair.</summary>
    public static bool IsKnown(string area, string action) =>
        Areas.Any(a => string.Equals(a.Area, area, StringComparison.OrdinalIgnoreCase) &&
                       a.Actions.Contains(action, StringComparer.OrdinalIgnoreCase));

    /// <summary>A readable action name: "read" → "Read", "update.own" → "Update own".</summary>
    public static string ActionLabel(string action) => action switch
    {
        PermissionActions.UpdateOwn => "Update own",
        PermissionActions.DeleteOwn => "Delete own",
        _ => char.ToUpperInvariant(action[0]) + action[1..],
    };
}

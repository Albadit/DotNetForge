using DotNetForge.Shared.Constants;

namespace DotNetForge.Shared.Authorization;

/// <summary>
/// The default permission baseline for the six built-in roles, encoding the canonical Permission
/// Matrix (user_roles_permissions.md). This is the single source of truth that both the Core
/// <c>PermissionService</c> and the Data seeder consume, so runtime checks and seeded grants always
/// agree. It lives in Shared as declarative reference data (keeping Data independent of Core).
/// Custom roles are evaluated from their stored grants, not this matrix.
/// </summary>
public static class PermissionMatrix
{
    private static readonly HashSet<string> ContentAndMedia = new(StringComparer.OrdinalIgnoreCase)
    {
        PermissionAreas.CollectionTypes,
        PermissionAreas.SingleTypes,
        PermissionAreas.Media,
    };

    private static readonly HashSet<string> EditorContentActions = new(StringComparer.OrdinalIgnoreCase)
    {
        PermissionActions.Read, PermissionActions.Create, PermissionActions.Update,
        PermissionActions.Delete, PermissionActions.Publish,
        PermissionActions.UpdateOwn, PermissionActions.DeleteOwn,
    };

    private static readonly HashSet<string> AuthorContentActions = new(StringComparer.OrdinalIgnoreCase)
    {
        PermissionActions.Read, PermissionActions.Create,
        PermissionActions.UpdateOwn, PermissionActions.DeleteOwn,
    };

    private static readonly HashSet<string> AdminManagedAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        PermissionAreas.Users, PermissionAreas.Roles, PermissionAreas.Settings,
        PermissionAreas.Api, PermissionAreas.Webhooks, PermissionAreas.Plugins,
    };

    /// <summary>Evaluates whether a built-in <paramref name="role"/> grants the action by default.</summary>
    public static bool IsGranted(string role, string area, string action)
    {
        // Super Admin always has every capability and cannot have any removed.
        if (Eq(role, Roles.SuperAdmin))
        {
            return true;
        }

        // Public and Authenticated: read-only access to content/media (public/permitted).
        if (Eq(role, Roles.Public) || Eq(role, Roles.Authenticated))
        {
            return IsRead(action) && ContentAndMedia.Contains(area);
        }

        if (Eq(role, Roles.Author))
        {
            return ContentAndMedia.Contains(area) && AuthorContentActions.Contains(action);
        }

        if (Eq(role, Roles.Editor))
        {
            return ContentAndMedia.Contains(area) && EditorContentActions.Contains(action);
        }

        if (Eq(role, Roles.Admin))
        {
            if (ContentAndMedia.Contains(area))
            {
                return EditorContentActions.Contains(action);
            }

            if (AdminManagedAreas.Contains(area))
            {
                return true;
            }

            // Audit log viewing and extension reading are allowed; installing/removing extensions
            // and core updates are Super Admin only.
            if (Eq(area, PermissionAreas.AuditLogs) || Eq(area, PermissionAreas.Extensions))
            {
                return IsRead(action);
            }

            return false;
        }

        // Unknown / custom role: no default grants (evaluated from stored RolePermission rows).
        return false;
    }

    /// <summary>
    /// Enumerates the concrete (area, action) grants a built-in role receives by default, used by
    /// the database seeder to materialize <c>RolePermission</c> rows.
    /// </summary>
    public static IEnumerable<(string Area, string Action)> GrantsFor(string role)
    {
        var actions = new[]
        {
            PermissionActions.Read, PermissionActions.Create, PermissionActions.Update,
            PermissionActions.Delete, PermissionActions.Publish, PermissionActions.UpdateOwn,
            PermissionActions.DeleteOwn, PermissionActions.Manage, PermissionActions.Configure,
            PermissionActions.Install,
        };

        foreach (var area in PermissionAreas.All)
        {
            foreach (var action in actions)
            {
                if (IsGranted(role, area, action))
                {
                    yield return (area, action);
                }
            }
        }
    }

    private static bool IsRead(string action) => Eq(action, PermissionActions.Read);

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

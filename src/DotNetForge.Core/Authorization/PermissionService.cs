using DotNetForge.Abstractions.Authorization;
using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Stores;

namespace DotNetForge.Core.Authorization;

/// <summary>
/// Default <see cref="IPermissionService"/>. Tenant checks read the roles' stored grants (<see cref="IRoleGrantStore"/>),
/// so custom roles and edited built-in roles take effect; <c>Super Admin</c> always has every grant. A role name the
/// tenant does not have falls back to the reference <see cref="PermissionMatrix"/> (which grants unknown names
/// nothing). A user's effective permissions are the union over all assigned roles (most-permissive wins).
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly IRoleGrantStore? _grants;

    /// <summary>Matrix-only evaluation (no stored grants) - for tests and tools.</summary>
    public PermissionService()
    {
    }

    public PermissionService(IRoleGrantStore grants)
    {
        _grants = grants;
    }

    public bool Has(string role, string area, string action)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        return PermissionMatrix.IsGranted(role, area, action);
    }

    public bool HasAny(IEnumerable<string> roles, string area, string action)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Any(role => Has(role, area, action));
    }

    public bool HasAny(Guid tenantId, IEnumerable<string> roles, string area, string action)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var names = roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (names.Contains(Roles.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_grants is null || tenantId == Guid.Empty || string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(action))
        {
            return HasAny(names, area, action);
        }

        var grants = _grants.GetGrants(tenantId);
        var key = PermissionCatalog.Key(area, action);
        return names.Any(role => grants.TryGetValue(role, out var granted)
            ? granted.Contains(key)
            : PermissionMatrix.IsGranted(role, area, action));
    }
}
